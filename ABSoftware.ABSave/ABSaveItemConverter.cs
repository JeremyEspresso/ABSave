﻿using ABSoftware.ABSave.Converters;
using ABSoftware.ABSave.Deserialization;
using ABSoftware.ABSave.Exceptions;
using ABSoftware.ABSave.Mapping;
using ABSoftware.ABSave.Serialization;
using System;
using System.ComponentModel;
using System.Reflection;

namespace ABSoftware.ABSave
{
    /// <summary>
    /// Serializes items (and their attributes) in an ABSave document.
    /// </summary>
    public static class ABSaveItemConverter
    {
        public static void SerializeWithAttribute(object obj, Type specifiedType, ABSaveWriter writer) => SerializeWithAttribute(obj, obj.GetType(), specifiedType, writer);

        public static void SerializeWithAttribute(object obj, Type actualType, Type specifiedType, ABSaveWriter writer)
        {
            if (SerializeAttribute(obj, actualType, specifiedType, writer)) return;
            SerializeWithoutAttribute(obj, actualType, writer);
        }

        public static void SerializeWithoutAttribute(object obj, Type actualType, ABSaveWriter writer)
        {
            if (writer.Settings.AutoCheckTypeConverters && ABSaveUtils.TryFindConverterForType(writer.Settings, actualType, out ABSaveTypeConverter typeConverter))
                typeConverter.Serialize(obj, actualType, writer);

            else ABSaveObjectConverter.Serialize(obj, actualType, writer);
        }

        public static object DeserializeWithAttribute(Type specifiedType, ABSaveReader reader)
        {
            var actualType = DeserializeAttribute(reader, specifiedType);
            if (actualType == null) return null;

            return DeserializeWithoutAttribute(actualType, reader);
        }

        public static object DeserializeWithoutAttribute(Type actualType, ABSaveReader reader)
        {
            if (reader.Settings.AutoCheckTypeConverters && ABSaveUtils.TryFindConverterForType(reader.Settings, actualType, out ABSaveTypeConverter typeConverter))
                return typeConverter.Deserialize(actualType, reader);

            return ABSaveObjectConverter.Deserialize(actualType, reader, null);
        }

        public static bool SerializeAttribute(object obj, Type actualType, Type specifiedType, ABSaveWriter writer)
        {
            if (obj == null)
            {
                writer.WriteNullAttribute();
                return true;
            }

            if (specifiedType.IsValueType)
            {
                // NOTE: Because of nullable's unique behaviour with boxing (resulting in a different "actual type"),
                //       they must be handled specially here, to make make sure we write an attribute if their value isn't null.
                if (specifiedType.IsGenericType && specifiedType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    writer.WriteMatchingTypeAttribute();
            } 
            else if (specifiedType == actualType) writer.WriteMatchingTypeAttribute();
            else writer.WriteDifferentTypeAttribute();

            return false;
        }

        /// <summary>
        /// Returns the actual type of data specified by the attribute, or null if there is none.
        /// </summary>
        public static Type DeserializeAttribute(ABSaveReader reader, Type specifiedType)
        {
            if (specifiedType.IsValueType)
            {
                if (specifiedType.IsGenericType && specifiedType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    return specifiedType.GetGenericArguments()[0];

                return specifiedType;
            }

            return reader.ReadByte() switch
            {
                1 => null,
                2 => specifiedType,
                3 => TypeTypeConverter.Instance.DeserializeClosedType(reader),
                _ => throw new ABSaveInvalidDocumentException(reader.Source.Position),
            };
        }

        static void SerializeTypeBeforeItem(ABSaveWriter writer, Type specifiedType, Type actualType)
        {
            // If the specified type is a value type, then there's no need to write any type information about it, since we know for certain nothing can inherit from it.
            if (!specifiedType.IsValueType)
            {
                // Remember that if the main part of the type is the same, the generics cannot be different, it's only if the main part is different do we need to write generics as well.
                if (actualType != specifiedType)
                {
                    writer.WriteDifferentTypeAttribute();
                    TypeTypeConverter.Instance.SerializeClosedType(actualType, writer);
                }
                else writer.WriteMatchingTypeAttribute();
            }
        }
    }
}