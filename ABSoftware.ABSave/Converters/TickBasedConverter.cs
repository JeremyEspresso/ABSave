﻿using ABCo.ABSave.Deserialization;
using ABCo.ABSave.Mapping;
using ABCo.ABSave.Mapping.Generation;
using ABCo.ABSave.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace ABCo.ABSave.Converters
{
    public class TickBasedConverter : Converter
    {
        TicksType _type;

        public override void Serialize(object obj, Type actualType, ref BitTarget header)
        {
            switch (_type)
            {
                case TicksType.DateTime:
                    SerializeTicks(((DateTime)obj).Ticks, header.Serializer);
                    break;
                case TicksType.TimeSpan:
                    SerializeTicks(((TimeSpan)obj).Ticks, header.Serializer);
                    break;
            }
        }

        public static void SerializeTicks(long ticks, ABSaveSerializer serializer) => serializer.WriteInt64(ticks);

        public override object Deserialize(Type actualType, ref BitSource header)
        {
            return _type switch
            {
                TicksType.DateTime => new DateTime(DeserializeTicks(header.Deserializer)),
                TicksType.TimeSpan => new TimeSpan(DeserializeTicks(header.Deserializer)),
                _ => throw new Exception("Invalid tick-based type"),
            };
        }

        public static long DeserializeTicks(ABSaveDeserializer deserializer) => deserializer.ReadInt64();

        public override void Initialize(InitializeInfo info) =>
            _type = info.Type == typeof(DateTime) ? TicksType.DateTime : TicksType.TimeSpan;

        enum TicksType
        {
            DateTime,
            TimeSpan
        }

        public override bool AlsoConvertsNonExact => false;
        public override Type[] ExactTypes { get; } = new Type[]
        {
            typeof(DateTime),
            typeof(TimeSpan)
        };
    }
}
