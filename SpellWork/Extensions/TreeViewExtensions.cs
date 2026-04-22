using SpellWork.Spell;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpellWork.Extensions
{
    public static class TreeViewExtensions
    {
        /// <summary>
        /// Returns the value of the collection of selected items
        /// </summary>
        /// <param name="tv"></param>
        /// <returns></returns>
        public static uint[] GetMask(this TreeView tv)
        {
            var val = new uint[4];
            foreach (var node in tv.Nodes.Cast<TreeNode>().Where(node => node.Checked))
            {
                if (node.Index < 32)
                    val[0] += 1U << node.Index;
                else if (node.Index < 64)
                    val[1] += 1U << (node.Index - 32);
                else if (node.Index < 96)
                    val[2] += 1U << (node.Index - 64);
                else
                    val[3] += 1U << (node.Index - 96);
            }
            return val;
        }

        /// <summary>
        /// Check items of the collection...
        /// </summary>
        /// <param name="tv"></param>
        /// <param name="mask"></param>
        public static void SetMask(this TreeView tv, uint[] mask)
        {
            ProcInfo.Update = false;

            for (var i = 0; i < tv.Nodes.Count; ++i)
            {
                if (i < 32)
                    tv.Nodes[i].Checked = ((mask[0] / (1 << i)) % 2) != 0;
                else if (i < 64)
                    tv.Nodes[i].Checked = ((mask[1] / (1 << (i - 32))) % 2) != 0;
                else if (i < 96)
                    tv.Nodes[i].Checked = ((mask[2] / (1 << (i - 64))) % 2) != 0;
                else
                    tv.Nodes[i].Checked = ((mask[3] / (1 << (i - 96))) % 2) != 0;
            }

            ProcInfo.Update = true;
        }

        private const int TVIF_STATE = 0x8;
        private const int TVIS_STATEIMAGEMASK = 0xF000;
        private const int TV_FIRST = 0x1100;
        private const int TVM_SETITEM = TV_FIRST + 63;

        [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Auto)]
        private struct TVITEM
        {
            public int mask;
            public IntPtr hItem;
            public int state;
            public int stateMask;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszText;
            public int cchTextMax;
            public int iImage;
            public int iSelectedImage;
            public int cChildren;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam,
                                                 ref TVITEM lParam);

        /// <summary>
        /// Hides the checkbox for the specified node on a TreeView control.
        /// </summary>
        public static void HideCheckBox(this TreeNode node)
        {
            TVITEM tvi = new TVITEM();
            tvi.hItem = node.Handle;
            tvi.mask = TVIF_STATE;
            tvi.stateMask = TVIS_STATEIMAGEMASK;
            tvi.state = 0;
            SendMessage(node.TreeView.Handle, TVM_SETITEM, IntPtr.Zero, ref tvi);
        }
    }
}
