﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microwalk.TraceEntryTypes
{
    /// <summary>
    /// A memory allocation.
    /// </summary>
    public class Allocation : TraceEntry
    {
        public override TraceEntryTypes EntryType =>  TraceEntryTypes.Allocation;

        protected override void Init(FastBinaryReader reader)
        {
            Id = reader.ReadInt32();
            Size = reader.ReadUInt32();
            Address = reader.ReadUInt64();
        }

        protected override void Store(BinaryWriter writer)
        {
            writer.Write(Id);
            writer.Write(Size);
            writer.Write(Address);
        }
        
        /// <summary>
        /// The ID of the allocated block.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The size of the allocated memory.
        /// </summary>
        public uint Size { get; set; }

        /// <summary>
        /// The address of the allocated memory.
        /// </summary>
        public ulong Address { get; set; }
    }
}
