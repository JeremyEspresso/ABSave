﻿using ABSoftware.ABSave.Deserialization;
using ABSoftware.ABSave.Serialization;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ABSoftware.ABSave.Converters
{
    public class TypeTypeConverter : ABSaveTypeConverter
    {
        public static TypeTypeConverter Instance = new TypeTypeConverter();
        private TypeTypeConverter() { }

        public override bool HasExactType => false;
        public override bool CheckCanConvertType(Type type) => type.IsSubclassOf(typeof(Type));

        public override void Serialize(object obj, Type type, ABSaveWriter writer) => SerializeType((Type)obj, writer);
        public override object Deserialize(Type type, ABSaveReader reader) => DeserializeType(reader);

        public void SerializeType(Type type, ABSaveWriter writer)
        {
            if (writer.Settings.CacheTypesAndAssemblies && SerializeKeyBeforeType(type, writer)) return;
            SerializeTypeMainPart(type, type.IsGenericType ? type.GetGenericTypeDefinition() : type, writer);
            SerializeGenericPart(type, writer);
        }

        public void SerializeClosedType(Type type, ABSaveWriter writer)
        {
            if (writer.Settings.CacheTypesAndAssemblies && SerializeKeyBeforeType(type, writer)) return;
            SerializeTypeMainPart(type, type.IsGenericType ? type.GetGenericTypeDefinition() : type, writer);
            SerializeClosedGenericPart(type, writer);
        }

        public void SerializeTypeMainPart(Type type, Type genericType, ABSaveWriter writer)
        {
            AssemblyTypeConverter.Instance.Serialize(type.Assembly, typeof(Assembly), writer);
            writer.WriteText(genericType.FullName);
        }

        public void SerializeGenericPart(Type type, ABSaveWriter writer)
        {
            if (type.IsGenericType)
            {
                var generics = type.GetGenericArguments();
                for (int i = 0; i < generics.Length; i++)
                {
                    if (generics[i].IsGenericParameter) writer.WriteByte(1);
                    else
                    {
                        writer.WriteByte(0);
                        SerializeType(generics[i], writer);
                    }
                }
            }
        }

        public void SerializeClosedGenericPart(Type type, ABSaveWriter writer)
        {
            if (type.IsGenericType)
            {
                var generics = type.GetGenericArguments();
                for (int i = 0; i < generics.Length; i++)
                    SerializeClosedType(generics[i], writer);
            }
        }

        public Type DeserializeType(ABSaveReader reader)
        {
            var cachedType = DeserializeKeyBeforeType(reader, out uint key);
            if (cachedType != null) return cachedType;

            var mainPart = DeserializeTypeMainPart(reader);
            var withGenerics = DeserializeGenericPart(mainPart, reader);

            if (key != uint.MaxValue) reader.CachedTypes.Add(withGenerics);
            return withGenerics;
        }

        public Type DeserializeClosedType(ABSaveReader reader)
        {
            var cachedType = DeserializeKeyBeforeType(reader, out uint key);
            if (cachedType != null) return cachedType;

            var mainPart = DeserializeTypeMainPart(reader);
            var withGenerics = DeserializeClosedGenericPart(mainPart, reader);

            if (key != uint.MaxValue) reader.CachedTypes.Add(withGenerics);
            return withGenerics;
        }

        public Type DeserializeTypeMainPart(ABSaveReader reader)
        {
            var assembly = (Assembly)AssemblyTypeConverter.Instance.Deserialize(typeof(Assembly), reader);
            var typeName = reader.ReadString();

            return assembly.GetType(typeName);
        }

        public Type DeserializeGenericPart(Type mainPart, ABSaveReader reader)
        {
            if (mainPart.IsGenericType)
            {
                var parameters = mainPart.GetGenericArguments();
               
                for (int i = 0; i < parameters.Length; i++)
                    if (reader.ReadByte() == 0)
                        parameters[i] = Instance.DeserializeType(reader);

                return mainPart.MakeGenericType(parameters);
            }
            else return mainPart;
        }

        public Type DeserializeClosedGenericPart(Type mainPart, ABSaveReader reader)
        {
            if (mainPart.IsGenericType)
            {
                var parameters = mainPart.GetGenericArguments();

                for (int i = 0; i < parameters.Length; i++)
                    parameters[i] = Instance.DeserializeType(reader);

                return mainPart.MakeGenericType(parameters);
            }
            else return mainPart;
        }

        static bool SerializeKeyBeforeType(Type type, ABSaveWriter writer)
        {
            var successful = writer.CachedTypes.TryGetValue(type, out int key);

            if (successful)
            {
                writer.WriteInt32((uint)key);
                return true;
            }
            else if (writer.CachedTypes.Count == int.MaxValue)
                writer.WriteInt32(uint.MaxValue);
            else
            {
                int size = writer.CachedAssemblies.Count;
                writer.CachedTypes.Add(type, size);
                writer.WriteLittleEndianInt32(size, ABSaveUtils.GetRequiredNoOfBytesToStoreNumber(size));
            }

            return false;
        }

        static Type DeserializeKeyBeforeType(ABSaveReader reader, out uint key)
        {
            if (reader.Settings.CacheTypesAndAssemblies)
            {
                key = reader.ReadLittleEndianInt32(ABSaveUtils.GetRequiredNoOfBytesToStoreNumber(reader.CachedAssemblies.Count));
                if (key < reader.CachedTypes.Count) return reader.CachedTypes[(int)key];
            }

            key = 0;
            return null;
        }
    }
}
