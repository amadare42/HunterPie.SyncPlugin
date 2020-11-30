using System;
using HunterPie.Core;
using HunterPie.Core.Definitions;
using Plugin.Sync.Util;

namespace Plugin.Sync.Model
{
    public class AilmentModel : IEquatable<AilmentModel>
    {
        public int Index { get; set; }
        
        public float MaxDuration { get; set; }
        public float Duration { get; set; }

        public float MaxBuildup { get; set; }
        public float Buildup { get; set; }
        public uint Counter { get; set; }
        
        public sMonsterAilment ToDomain() => new sMonsterAilment
        {
            MaxBuildup = this.MaxBuildup,
            Duration = this.Duration,
            Buildup = this.Buildup,
            Counter = this.Counter
        };

        public static AilmentModel FromDomain(Ailment a, int ailmentIndex) => new AilmentModel
        {
            Index = ailmentIndex,
            Buildup = a.Buildup,
            MaxBuildup = a.MaxBuildup,
            Counter = a.Counter,
            Duration = a.Duration,
            MaxDuration = a.MaxDuration
        };

        public bool Equals(AilmentModel other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.MaxDuration.Equals(other.MaxDuration)
                   && this.Index == other.Index
                   && this.Duration.EqualsDelta(other.Duration, 0.9f)
                   && this.MaxBuildup.EqualsDelta(other.MaxBuildup, 0.9f)
                   && this.Buildup.EqualsDelta(other.Buildup, 0.9f)
                   && this.Counter == other.Counter;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((AilmentModel) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = this.MaxDuration.GetHashCode();
                hashCode = (hashCode * 397) ^ ((int)this.Duration).GetHashCode();
                hashCode = (hashCode * 397) ^ this.MaxBuildup.GetHashCode();
                hashCode = (hashCode * 397) ^ this.Buildup.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) this.Counter;
                hashCode = (hashCode * 397) ^ this.Index;
                return hashCode;
            }
        }

        public AilmentModel Clone()
        {
            return new AilmentModel
            {
                Index = this.Index,
                Buildup = this.Buildup,
                Counter = this.Counter,
                Duration = this.Duration,
                MaxBuildup = this.MaxBuildup,
                MaxDuration = this.MaxDuration
            };
        }
    }
}
