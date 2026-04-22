using SpellWork.Spell;
using System.Globalization;

namespace SpellWork.Database
{
    public sealed class SpellProcEntry
    {
        public int SpellId;
        public string SpellName;
        public SpellSchoolMask SchoolMask;          // if nonzero - bitmask for matching proc condition based on spell's school
        public SpellFamilyNames SpellFamilyName;    // if nonzero - for matching proc condition based on candidate spell's SpellFamilyName
        public uint[] SpellFamilyMask;              // if nonzero - bitmask for matching proc condition based on candidate spell's SpellFamilyFlags
        public ProcFlags ProcFlags;                 // if nonzero - owerwrite procFlags field for given Spell.dbc entry, bitmask for matching proc condition
        public ProcFlags2 ProcFlags2;               // if nonzero - owerwrite procFlags field for given Spell.dbc entry, bitmask for matching proc condition
        public ProcFlagsSpellType SpellTypeMask;    // if nonzero - bitmask for matching proc condition based on candidate spell's damage/heal effects
        public ProcFlagsSpellPhase SpellPhaseMask;  // if nonzero - bitmask for matching phase of a spellcast on which proc occurs
        public ProcFlagsHit HitMask;                // if nonzero - bitmask for matching proc condition based on hit result
        public ProcAttributes AttributesMask;       // bitmask, see ProcAttributes
        public uint DisableEffectsMask;             // bitmask
        public float ProcsPerMinute;                // if nonzero - chance to proc is equal to value * aura caster's weapon speed / 60
        public float Chance;                        // if nonzero - owerwrite procChance field for given Spell.dbc entry, defines chance of proc to occur, not used if ProcsPerMinute set
        public uint Cooldown;                       // if nonzero - cooldown in secs for aura proc, applied to aura
        public uint Charges;                        // if nonzero - owerwrite procCharges field for given Spell.dbc entry, defines how many times proc can occur before aura remove, 0 - infinite

        public string[] ToArray()
        {
            return new[]
            {
                SpellId.ToString(),
                SpellName,
                SchoolMask != SpellSchoolMask.SPELL_SCHOOL_MASK_NONE ? SchoolMask.ToString().Replace("SPELL_SCHOOL_MASK_", "") : "",
                SpellFamilyName != SpellFamilyNames.SPELLFAMILY_GENERIC ? SpellFamilyName.ToString().Replace("SPELLFAMILY_", "") : "",
                $"0x{SpellFamilyMask[0]:X8}",
                $"0x{SpellFamilyMask[1]:X8}",
                $"0x{SpellFamilyMask[2]:X8}",
                $"0x{SpellFamilyMask[3]:X8}",
                ProcFlags.ToString().Replace("PROC_FLAG_", ""),
                ProcFlags2.ToString().Replace("PROC_FLAG_2_", ""),
                SpellTypeMask.ToString().Replace("PROC_SPELL_TYPE_", ""),
                SpellPhaseMask.ToString().Replace("PROC_SPELL_PHASE_", ""),
                HitMask.ToString().Replace("PROC_HIT_", ""),
                AttributesMask.ToString().Replace("PROC_ATTR_", ""),
                $"0x{DisableEffectsMask:X8}",
                ProcsPerMinute.ToString(CultureInfo.InvariantCulture),
                Chance != 0.0f ? Chance.ToString(CultureInfo.InvariantCulture) : "",
                Cooldown != 0 ? Cooldown.ToString() : "",
                Charges != 0 ? Charges.ToString() : ""
            };
        }
    }
}
