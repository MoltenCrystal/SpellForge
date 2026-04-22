using SpellWork.DBC.Structures;
using SpellWork.Extensions;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SpellWork.Spell
{
    public static class ProcInfo
    {
        public static SpellInfo SpellProc { get; set; }
        public static bool Update = true;

        public static void Fill(TreeView familyTree, SpellFamilyNames spellfamily)
        {
            familyTree.BeginUpdate();

            familyTree.Nodes.Clear();

            var spells = from spell in DBC.DBC.SpellInfoStore.Values
                         where spell.SpellFamilyName == (uint)spellfamily

                         join sk in DBC.DBC.SkillLineAbility.Values on spell.ID equals sk.Spell into temp1
                         from skill in temp1.DefaultIfEmpty(new SkillLineAbilityEntry())

                         join skl in DBC.DBC.SkillLine on skill.SkillLine equals skl.Key into temp2
                         from skillLine in temp2.DefaultIfEmpty()

                         select new
                         {
                             spell,
                             skill.SkillLine,
                             skillLine = skillLine.Value
                         };

            for (var i = 0; i < 128; ++i)
            {
                var mask = new uint[4];
                mask[i / 32] = 1u << (i % 32);
                var node = new TreeNode
                {
                    Text = $"0x{mask[3]:X8} {mask[2]:X8} {mask[1]:X8} {mask[0]:X8}",
                    ImageKey = "family.ico"
                };
                familyTree.Nodes.Add(node);
            }

            foreach (var elem in spells)
            {
                var spell = elem.spell;
                var isSkill = elem.SkillLine != 0;

                var name = new StringBuilder();
                var toolTip = new StringBuilder();

                name.AppendFormat("{0} - {1} ", spell.ID, spell.NameAndSubname);

                toolTip.AppendFormatLine("Spell Name: {0}",  spell.NameAndSubname);
                toolTip.AppendFormatLine("Description: {0}", spell.Description);
                toolTip.AppendFormatLine("ToolTip: {0}",     spell.Tooltip);

                if (isSkill)
                {
                    name.AppendFormat("(Skill: ({0}) {1}) ", elem.SkillLine, elem.skillLine.DisplayName);

                    toolTip.AppendLine();
                    toolTip.AppendFormatLine("Skill Name: {0}", elem.skillLine.DisplayName);
                    toolTip.AppendFormatLine("Description: {0}", elem.skillLine.Description);
                }

                name.AppendFormat("({0})", ((SpellSchoolMask)spell.SchoolMask).ToString().NormalizeString("MASK_"));

                foreach (TreeNode node in familyTree.Nodes)
                {
                    if ((spell.SpellClassMask[node.Index / 32] & (1u << (node.Index % 32))) == 0)
                        continue;

                    var child       = node.Nodes.Add(name.ToString());
                    child.HideCheckBox();
                    child.Name      = spell.ID.ToString();
                    child.ImageKey  = isSkill ? "plus.ico" : "munus.ico";
                    child.ForeColor = isSkill ? Color.Blue : Color.Red;
                    child.ToolTipText = toolTip.ToString();
                }
            }

            familyTree.EndUpdate();
        }
    }
}
