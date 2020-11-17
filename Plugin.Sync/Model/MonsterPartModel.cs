using System;
using HunterPie.Core;
using HunterPie.Core.Definitions;

namespace Plugin.Sync.Model
{
    public class MonsterPartModel : IEquatable<MonsterPartModel>
    {
        public int Index { get; set; }
        public float MaxHealth { get; set; }
        public float Health { get; set; }
        public int Counter { get; set; }
        
        // TODO: this can be inferred 
        public bool IsRemovable { get; set; }

        public float TenderizeMaxDuration { get; set; }
        public float TenderizeDuration { get; set; }

        public MonsterPartModel Clone()
        {
            return new MonsterPartModel
            {
                Index = this.Index,
                MaxHealth = this.MaxHealth,
                Counter = this.Counter,
                Health = this.Health,
                IsRemovable = this.IsRemovable,
                TenderizeDuration = this.TenderizeDuration,
                TenderizeMaxDuration = this.TenderizeMaxDuration
            };
        }

        public sMonsterPartData ToDomain() => new sMonsterPartData
        {
            MaxHealth = this.MaxHealth, 
            Health = this.Health, 
            Counter = this.Counter
        };

        public static MonsterPartModel FromDomain(Part p, int index) => new MonsterPartModel
        {
            Index = index,
            Counter = p.BrokenCounter, 
            Health = p.Health,
            IsRemovable = p.IsRemovable, 
            MaxHealth = p.TotalHealth,
            TenderizeDuration = p.TenderizeDuration,
            TenderizeMaxDuration = p.TenderizeMaxDuration
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

            return this.MaxHealth.Equals(other.MaxHealth) 
                   && this.Index == other.Index 
                   && this.Health.Equals(other.Health) 
                   && this.Counter == other.Counter 
                   && this.IsRemovable == other.IsRemovable 
                   && this.TenderizeMaxDuration.Equals(other.TenderizeMaxDuration) 
                   && this.TenderizeDuration.Equals(other.TenderizeDuration);
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
                var hashCode = this.MaxHealth.GetHashCode();
                hashCode = (hashCode * 397) ^ this.Index;
                hashCode = (hashCode * 397) ^ this.Health.GetHashCode();
                hashCode = (hashCode * 397) ^ this.Counter;
                hashCode = (hashCode * 397) ^ this.IsRemovable.GetHashCode();
                hashCode = (hashCode * 397) ^ this.TenderizeMaxDuration.GetHashCode();
                hashCode = (hashCode * 397) ^ this.TenderizeDuration.GetHashCode();
                return hashCode;
            }
        }
    }
}
