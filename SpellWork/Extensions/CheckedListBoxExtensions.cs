using System;
using System.Windows.Forms;

namespace SpellWork.Extensions
{
    public static class CheckedListBoxExtensions
    {
        sealed class CheckedListBoxEnumItem
        {
            public string Name { get; private set; }
            public uint Value { get; private set; }
            public int WordIndex { get; private set; }

            public CheckedListBoxEnumItem(string name, uint value, int wordIndex)
            {
                Name = name;
                Value = value;
                WordIndex = wordIndex;
            }

            public override string ToString()
            {
                return Name;
            }
        }

        public static void SetCheckedItemFromFlag(this CheckedListBox name, uint value, int wordIndex = 0)
        {
            for (var i = 0; i < name.Items.Count; ++i)
            {
                var item = (CheckedListBoxEnumItem)name.Items[i];
                if (item.WordIndex != wordIndex)
                    continue;

                name.SetItemChecked(i, (value & item.Value) != 0);
            }
        }

        public static uint GetFlagsValue(this CheckedListBox name, int wordIndex = 0)
        {
            uint val = 0;
            for (var i = 0; i < name.CheckedItems.Count; ++i)
            {
                var item = (CheckedListBoxEnumItem)name.CheckedItems[i];
                if (item.WordIndex != wordIndex)
                    continue;

                val |= item.Value;
            }

            return val;
        }

        public static void SetFlags<T>(this CheckedListBox clb, string remove = null)
        {
            clb.SetFlags(typeof(T), remove);
        }

        public static void SetFlags(this CheckedListBox clb, Type type, string remove = null)
        {
            clb.SetFlags(Enum.GetValues(type), remove);
        }

        public static void SetFlags(this CheckedListBox clb, Array values, string remove = null)
        {
            clb.Items.Clear();
            clb.Items.AddRange(GenerateCheckedListBoxEnumItems(values, remove, 0));
        }

        public static void AddFlags<T>(this CheckedListBox clb, string remove = null, int wordIndex = 0)
        {
            clb.AddFlags(typeof(T), remove, wordIndex);
        }

        public static void AddFlags(this CheckedListBox clb, Type type, string remove = null, int wordIndex = 0)
        {
            clb.AddFlags(Enum.GetValues(type), remove, wordIndex);
        }

        public static void AddFlags(this CheckedListBox clb, Array values, string remove = null, int wordIndex = 0)
        {
            clb.Items.AddRange(GenerateCheckedListBoxEnumItems(values, remove, wordIndex));
        }

        private static CheckedListBoxEnumItem[] GenerateCheckedListBoxEnumItems(Array values, string remove = null, int wordIndex = 0)
        {
            var items = new CheckedListBoxEnumItem[values.Length];
            for (int i = 0; i < values.Length; ++i)
                items[i] = new CheckedListBoxEnumItem(values.GetValue(i).ToString().NormalizeString(remove), Convert.ToUInt32(values.GetValue(i)), wordIndex);

            return items;
        }
    }
}
