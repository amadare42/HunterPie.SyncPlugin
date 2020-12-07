using System;
using HunterPie.Core;
using Plugin.Sync.Util;

namespace Plugin.Sync.Model
{
    [JsonArrayObject]
    public class AilmentModel : IEquatable<AilmentModel>
    {
        [JsonArrayProp(Index = 0)]
        public int Index { get; set; }
        
        [JsonArrayProp(Index = 1)]
        public float Buildup { get; set; }

        public static AilmentModel FromDomain(Ailment a, int ailmentIndex) => new AilmentModel
        {
            Index = ailmentIndex,
            Buildup = a.Buildup
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

            return this.Index == other.Index
                   && this.Buildup.EqualsDelta(other.Buildup, 0.9f);
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
                var hashCode = this.Buildup.GetHashCode();
                hashCode = (hashCode * 397) ^ this.Index;
                return hashCode;
            }
        }

        public AilmentModel Clone()
        {
            return new AilmentModel
            {
                Index = this.Index,
                Buildup = this.Buildup
            };
        }
    }
}
