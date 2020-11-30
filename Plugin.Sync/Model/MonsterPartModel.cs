using System;
using HunterPie.Core;
using Plugin.Sync.Util;

namespace Plugin.Sync.Model
{
    public class MonsterPartModel : IEquatable<MonsterPartModel>
    {
        public int Index { get; set; }
        public float Health { get; set; }

        public MonsterPartModel Clone()
        {
            return new MonsterPartModel
            {
                Index = this.Index,
                Health = this.Health,
            };
        }

        public static MonsterPartModel FromDomain(Part p, int index) => new MonsterPartModel
        {
            Index = index,
            Health = p.Health,
        };

        public bool Equals(MonsterPartModel other)
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
                   && this.Health.EqualsDelta(other.Health, 0.9f);
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

            return Equals((MonsterPartModel) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = this.Index;
                hashCode = (hashCode * 397) ^ this.Health.GetHashCode();
                return hashCode;
            }
        }
    }
}
