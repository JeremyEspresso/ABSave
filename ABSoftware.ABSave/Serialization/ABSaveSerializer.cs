﻿using ABSoftware.ABSave;
using ABSoftware.ABSave.Converters;
using ABSoftware.ABSave.Exceptions;
using ABSoftware.ABSave.Helpers;
using ABSoftware.ABSave.Mapping;
using ABSoftware.ABSave.Mapping.Description;
using ABSoftware.ABSave.Mapping.Description.Attributes;
using ABSoftware.ABSave.Mapping.Generation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Serialization;

namespace ABSoftware.ABSave.Serialization
{
    /// <summary>
    /// The central object that everything in ABSave writes to. Provides facilties to write primitive types, including strings.
    /// </summary>
    public sealed partial class ABSaveSerializer
    {
        readonly Dictionary<MapItem, ObjectVersionInfo> _objectVersions = new Dictionary<MapItem, ObjectVersionInfo>();
        readonly Dictionary<MapItem, ConverterVersionInfo> _converterVersions = new Dictionary<MapItem, ConverterVersionInfo>();

        internal Dictionary<Assembly, int> SavedAssemblies = new Dictionary<Assembly, int>();
        internal Dictionary<Type, int> SavedTypes = new Dictionary<Type, int>();

        public Dictionary<Type, uint>? TargetVersions { get; private set; }
        public ABSaveMap Map { get; private set; } = null!;
        public ABSaveSettings Settings { get; private set; } = null!;
        public Stream Output { get; private set; } = null!;
        public bool ShouldReverseEndian { get; private set; }

        byte[]? _stringBuffer;

        public void Initialize(Stream output, ABSaveMap map, Dictionary<Type, uint>? targetVersions)
        {
            if (!output.CanWrite)
                throw new Exception("Cannot use unwriteable stream.");

            Output = output;

            Map = map;
            Settings = map.Settings;
            TargetVersions = targetVersions;

            ShouldReverseEndian = map.Settings.UseLittleEndian != BitConverter.IsLittleEndian;

            Reset();
        }

        public void Reset()
        {
            SavedAssemblies.Clear();
            SavedTypes.Clear();
            _objectVersions.Clear();
            _converterVersions.Clear();
        }

        public MapItemInfo GetRuntimeMapItem(Type type) => Map.GetRuntimeMapItem(type);

        public void SerializeRoot(object? obj)
        {
            SerializeItem(obj, Map.RootItem);
        }

        public void SerializeItem(object? obj, MapItemInfo item)
        {
            if (obj == null)
                WriteByte(0);

            else
            {
                var currentHeader = new BitTarget(this);
                SerializePossibleNullableItem(obj, item, ref currentHeader);
            }
        }

        public void SerializeItem(object? obj, MapItemInfo item, ref BitTarget header)
        {
            if (obj == null)
            {
                header.WriteBitOff();
                header.Apply();
            }

            else SerializePossibleNullableItem(obj, item, ref header);
        }

        public void SerializeExactNonNullItem(object obj, MapItemInfo item)
        {
            var currentHeader = new BitTarget(this);
            SerializeItemNoSetup(obj, item, ref currentHeader, true);
        }

        public void SerializeExactNonNullItem(object obj, MapItemInfo item, ref BitTarget header) => 
            SerializeItemNoSetup(obj, item, ref header, true);

        public void SerializePossibleNullableItem(object obj, MapItemInfo info, ref BitTarget header)
        {
            // Say it's "not null" if it is nullable.
            if (info.IsNullable) header.WriteBitOn();
            SerializeItemNoSetup(obj, info, ref header, info.IsNullable);
        }

        void SerializeItemNoSetup(object obj, MapItemInfo info, ref BitTarget header, bool skipHeader)
        {
            MapItem item = info._innerItem;
            ABSaveUtils.WaitUntilNotGenerating(item);

            switch (item)
            {
                case ConverterContext ctx:
                    SerializeConverterItem(obj, ctx, ref header, skipHeader);
                    break;
                case ObjectMapItem objMap:
                    SerializeObjectItem(obj, objMap, ref header, skipHeader);
                    break;
                case RuntimeMapItem runtime:
                    SerializeItemNoSetup(obj, new MapItemInfo(runtime.InnerItem, info.IsNullable), ref header, skipHeader);
                    break;
                default:
                    throw new Exception("ABSAVE: Unrecognized map item.");
            }
        }

        void SerializeConverterItem(object obj, ConverterContext ctx, ref BitTarget header, bool skipHeader)
        {
            var actualType = obj.GetType();

            // Write the null and inheritance bits.
            bool sameType = true;
            if (!skipHeader)
                sameType = WriteHeaderNullAndInheritance(actualType, ctx, ref header);

            // Write and get the info for a version, if necessary
            if (_converterVersions.TryGetValue(ctx, out ConverterVersionInfo info))
            {
                uint version = WriteNewVersionInfo(ctx, ref header);
                info = ConverterVersionInfo.CreateFromContext(version, ctx);
                _converterVersions.Add(ctx, info);
            }

            // Handle inheritance if needed.
            if (info.InheritanceInfo != null && !sameType)
            {
                SerializeActualType(obj, info.InheritanceInfo, ctx.ItemType, actualType, ref header);
                return;
            }

            // Apply the header if it's not being used.
            if (!info.UsesHeader)
                header.Apply();

            ctx._converter.Serialize(obj, actualType, ctx, ref header);
        }

        void SerializeObjectItem(object obj, ObjectMapItem item, ref BitTarget header, bool skipHeader)
        {
            var actualType = obj.GetType();

            // Write the null and inheritance bits.
            bool sameType = true;
            if (!skipHeader) 
                sameType = WriteHeaderNullAndInheritance(actualType, item, ref header);

            // Write and get the info for a version, if necessary
            if (_objectVersions.TryGetValue(item, out ObjectVersionInfo info))
            {
                uint version = WriteNewVersionInfo(item, ref header);
                info = MapGenerator.GetVersionOrAddNull(version, item);
                _objectVersions.Add(item, info);
            }

            // Handle inheritance if needed.
            if (info.InheritanceInfo != null && !sameType)
            {
                SerializeActualType(obj, info.InheritanceInfo, item.ItemType, actualType, ref header);
                return;
            }

            SerializeFromMembers(obj, info.Members!);
        }

        void SerializeFromMembers(object obj, ObjectMemberSharedInfo[] members)
        {
            for (int i = 0; i < members.Length; i++)
                SerializeItem(members[i].Accessor.Getter(obj), members[i].Map);
        }

        // Returns: Whether the type has changed.
        bool WriteHeaderNullAndInheritance(Type actualType, MapItem item, ref BitTarget target)
        {
            if (item.IsValueItemType) return true;

            target.WriteBitOn(); // Null

            bool sameType = item.ItemType == actualType;
            target.WriteBitWith(sameType);
            return sameType;
        }

        uint WriteNewVersionInfo(MapItem item, ref BitTarget target)
        {
            uint targetVersion = 0;

            // Try to get the custom target version and if there is none use the latest.
            if (TargetVersions?.TryGetValue(item.ItemType, out targetVersion) != true)
                targetVersion = item.HighestVersion;

            WriteCompressed(targetVersion, ref target);
            return targetVersion;
        }

        // Returns: Whether the sub-type was converted in here and we should return now.
        void SerializeActualType(object obj, SaveInheritanceAttribute info, Type baseType, Type actualType, ref BitTarget header)
        {
            switch (info.Mode)
            {
                case SaveInheritanceMode.Index:
                    if (!TryWriteListInheritance(info, actualType, ref header))                    
                        throw new UnsupportedSubTypeException(baseType, actualType);

                    break;
                case SaveInheritanceMode.Key:
                    WriteKeyInheritance(info, baseType, actualType, ref header);

                    break;
                case SaveInheritanceMode.IndexOrKey:
                    if (TryWriteListInheritance(info, actualType, ref header))
                        header.WriteBitOn();
                    else
                    {
                        header.WriteBitOff();
                        WriteKeyInheritance(info, baseType, actualType, ref header);
                    }

                    break;
            }

            // Serialize the actual type now.
            SerializeItemNoSetup(obj, GetRuntimeMapItem(actualType), ref header, true);
        }

        bool TryWriteListInheritance(SaveInheritanceAttribute info, Type actualType, ref BitTarget header)
        {
            if (info.IndexSerializeCache!.TryGetValue(actualType, out uint pos))
            {
                WriteCompressed(pos, ref header);
                return true;
            }
            
            return false;
        }

        void WriteKeyInheritance(SaveInheritanceAttribute info, Type baseType, Type actualType, ref BitTarget header)
        {
            string key = KeyInheritanceHandler.GetOrAddTypeKeyFromCache(baseType, actualType, info);
            WriteString(key, ref header);
        }

        void SerializeActualType(object obj, Type type)
        {
            var info = GetRuntimeMapItem(type);

            var newTarget = new BitTarget(this);
            SerializeItemNoSetup(obj, info, ref newTarget, true);
        }

        // TODO: Use map guides to implement proper "Type" handling via map.
        public void WriteType(Type type)
        {
            var header = new BitTarget(this);
            WriteType(type, ref header);
        }

        public static void WriteType(Type type, ref BitTarget header) => TypeConverter.Instance.SerializeType(type, ref header);

        public void WriteClosedType(Type type)
        {
            var header = new BitTarget(this);
            WriteClosedType(type, ref header);
        }

        public static void WriteClosedType(Type type, ref BitTarget header) => TypeConverter.Instance.SerializeClosedType(type, ref header);

        static bool ConverterUsesHeader(MapItem item, uint targetVersion) => 
            Unsafe.As<ConverterContext>(item)._converter.UsesHeaderForVersion(targetVersion);
    }
}
