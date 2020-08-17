﻿using ABSoftware.ABSave.Deserialization;
using ABSoftware.ABSave.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace ABSoftware.ABSave.Mapping
{
    public class ObjectMapItem : ABSaveMapItem
    {
        private int _itemsAdded = 0;
        public int NumberOfItems;

        internal bool Initialized;
        internal ABSaveMapItem[] Items;
        internal Dictionary<string, ABSaveMapItem> HashedItems;

        public ObjectMapItem(bool canBeNull, int numberOfItems) : base(canBeNull)
        {
            NumberOfItems = numberOfItems;
            Items = new ABSaveMapItem[numberOfItems];
            HashedItems = new Dictionary<string, ABSaveMapItem>(numberOfItems);
        }

        public ObjectMapItem AddItem(string name, ABSaveMapItem mapItem)
        {
            if (_itemsAdded == NumberOfItems) throw new Exception("ABSAVE: Too many items added to an object map, make sure to set the correct size in the constructor.");

            mapItem.Name = name;
            Items[_itemsAdded++] = mapItem;
            HashedItems.Add(name, mapItem);

            return this;
        }

        public ObjectMapItem AddItem<TObject, TItem>(string name, Func<TObject, TItem> getter, Action<TObject, TItem> setter, ABSaveMapItem mapItem)
        {
            mapItem.UseReflection = false;
            mapItem.Getter = container => getter((TObject)container);
            mapItem.Setter = (container, itm) => setter((TObject)container, (TItem)itm);
            mapItem.FieldType = typeof(TItem);
            return AddItem(name, mapItem);
        }

        public override void Serialize(object obj, Type type, ABSaveWriter writer) => ABSaveObjectConverter.Serialize(obj, type, writer, this);
        public override object Deserialize(Type type, ABSaveReader reader) => ABSaveObjectConverter.Deserialize(type, reader, this);
    }
}
