using DBFileReaderLib.Attributes;

namespace SpellWork.DBC.Structures
{
    public sealed class ItemXItemEffectEntry
    {
        [Index(true)]
        public uint ID;
        public int ItemEffectID;
        public int ItemID;
    }
}
