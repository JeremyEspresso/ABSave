﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ABCo.ABSave.Mapping.Description.Attributes
{
    /// <summary>
    /// Tells ABSave that it should serialize all the members of the given base type.
    /// If the type given also has a "SaveBaseMembers" attribute its base members will be serialized too.
    /// </summary>
    public class SaveBaseMembersAttribute : Attribute
    {
        public Type BaseType;

        public SaveBaseMembersAttribute(Type type) => BaseType = type;
    }
}
