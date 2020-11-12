using System;
using System.Collections.Generic;
using System.Linq;
using Plugin.Sync.Util;

namespace Plugin.Sync.Model
{
    public class MonsterModel : IEquatable<MonsterModel>
    {
        public string Id { get; set; }
        public int Index { get; set; }
        public List<MonsterPartModel> Parts { get; set; } = new List<MonsterPartModel>();
        public List<AilmentModel> Ailments { get; set; } = new List<AilmentModel>();

        public MonsterModel Clone()
        {
            return new MonsterModel
            {
                Id = this.Id,
                Index = this.Index,
                Parts = this.Parts.Select(p => p.Clone()).ToList(),
                Ailments = this.Ailments.Select(a => a.Clone()).ToList()
            };
        }

        public bool Equals(MonsterModel other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.Id == other.Id && this.Index == other.Index && this.Parts.AreEqual(other.Parts) && this.Ailments.AreEqual(other.Ailments);
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

            return Equals((MonsterModel) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (this.Id != null ? this.Id.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ this.Index;
                hashCode = (hashCode * 397) ^ (this.Parts != null ? this.Parts.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (this.Ailments != null ? this.Ailments.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}
