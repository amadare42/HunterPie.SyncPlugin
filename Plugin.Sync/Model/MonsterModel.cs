using System;
using System.Collections.Generic;
using System.Linq;
using Plugin.Sync.Util;

namespace Plugin.Sync.Model
{
    [JsonArrayObject]
    public class MonsterModel : IEquatable<MonsterModel>
    {
        [JsonArrayProp(Index = 0)]
        public string Id { get; set; }
        
        [JsonArrayProp(Index = 1)]
        public List<MonsterPartModel> Parts { get; set; } = new List<MonsterPartModel>();
        
        [JsonArrayProp(Index = 2)]
        public List<AilmentModel> Ailments { get; set; } = new List<AilmentModel>();

        [JsonArrayProp(Index = 3)]
        public int TotalHp { get; set; }
        
        public void UpdateWith(MonsterModel model)
        {
            var maxAilmentIdx = Math.Max(model.Ailments.MaxOrDefault(a => a.Index), this.Ailments.MaxOrDefault(a => a.Index));
            var maxPartIdx = Math.Max(model.Parts.MaxOrDefault(a => a.Index), this.Parts.MaxOrDefault(a => a.Index));
            
            this.Id = model.Id;
            this.Ailments = Enumerable.Range(0, maxAilmentIdx + 1)
                .Select(i =>
                    model.Ailments.FirstOrDefault(a => a.Index == i) ?? this.Ailments.FirstOrDefault(a => a.Index == i))
                .Where(e => e != null)
                .ToList();
            this.Parts = Enumerable.Range(0, maxPartIdx + 1)
                .Select(i =>
                    model.Parts.FirstOrDefault(a => a.Index == i) ?? this.Parts.FirstOrDefault(a => a.Index == i))
                .Where(e => e != null)
                .ToList();
            
            if (this.TotalHp != 0 && model.TotalHp != 0 && model.TotalHp < this.TotalHp)
            {
                this.TotalHp = model.TotalHp;
            }
        }

        public MonsterModel Clone()
        {
            return new MonsterModel
            {
                Id = this.Id,
                Parts = this.Parts.Select(p => p.Clone()).ToList(),
                Ailments = this.Ailments.Select(a => a.Clone()).ToList(),
                TotalHp = this.TotalHp
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

            return this.Id == other.Id 
                   && this.TotalHp == other.TotalHp
                   && this.Parts.AreEqual(other.Parts) 
                   && this.Ailments.AreEqual(other.Ailments);
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
                hashCode = (hashCode * 397) ^ (this.Parts != null ? this.Parts.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ this.TotalHp;
                hashCode = (hashCode * 397) ^ (this.Ailments != null ? this.Ailments.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}
