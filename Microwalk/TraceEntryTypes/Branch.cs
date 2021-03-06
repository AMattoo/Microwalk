﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microwalk.TraceEntryTypes
{
    /// <summary>
    /// A code branch.
    /// </summary>
    public class Branch : TraceEntry
    {
        public override TraceEntryTypes EntryType => TraceEntryTypes.Branch;

        protected override void Init(FastBinaryReader reader)
        {
            SourceImageId = reader.ReadInt32();
            SourceInstructionRelativeAddress = reader.ReadUInt32();
            DestinationImageId = reader.ReadInt32();
            DestinationInstructionRelativeAddress = reader.ReadUInt32();
            Taken = reader.ReadBoolean();
            BranchType = (BranchTypes)reader.ReadByte();
        }

        protected override void Store(BinaryWriter writer)
        {
            writer.Write(SourceImageId);
            writer.Write(SourceInstructionRelativeAddress);
            writer.Write(DestinationImageId);
            writer.Write(DestinationInstructionRelativeAddress);
            writer.Write(Taken);
            writer.Write((byte)BranchType);
        }

        /// <summary>
        ///  The image ID of the source instruction.
        /// </summary>
        public int SourceImageId { get; set; }

        /// <summary>
        /// The address of the source instruction, relative to the image start address.
        /// </summary>
        public uint SourceInstructionRelativeAddress { get; set; }

        /// <summary>
        /// The image ID of the destination instruction.
        /// </summary>
        public int DestinationImageId { get; set; }

        /// <summary>
        /// The address of the destination instruction, relative to the image start address.
        /// </summary>
        public uint DestinationInstructionRelativeAddress { get; set; }

        /// <summary>
        /// Tells whether the branch was taken.
        /// </summary>
        public bool Taken { get; set; }

        /// <summary>
        /// The type of the branching instruction.
        /// </summary>
        public BranchTypes BranchType { get; set; }

        /// <summary>
        /// The type of the branching instruction.
        /// </summary>
        public enum BranchTypes : byte
        {
            Jump = 0,
            Call = 1,
            Return = 2
        }
    }
}
