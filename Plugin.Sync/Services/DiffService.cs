using System.Collections.Generic;
using System.Linq;
using Plugin.Sync.Model;

namespace Plugin.Sync.Services
{
    /// <summary>
    /// Stateful service that will generate models that only contains differences between latest and provided models.
    /// </summary>
    public class DiffService
    {
        private readonly List<MonsterModel> state = new List<MonsterModel>();

        public List<MonsterModel> GetDiffs(List<MonsterModel> models)
        {
            var diffs = new List<MonsterModel>();
            
            foreach (var newModel in models)
            {
                var existing = this.state.FirstOrDefault(m => m.Id == newModel.Id);
                if (existing == null)
                {
                    this.state.Add(newModel);
                    diffs.Add(newModel);
                    continue;
                }
                
                var diff = GetDiffModel(existing, newModel);
                if (diff != null)
                {
                    diffs.Add(diff);
                    existing.UpdateWith(diff);
                }
            }
            
            return diffs;
        }

        private static MonsterModel GetDiffModel(MonsterModel existing, MonsterModel newModel)
        {
            if (existing == null) return newModel;
            if (newModel == null) return null;
            if (existing.Ailments.Count != newModel.Ailments.Count || existing.Parts.Count != newModel.Parts.Count)
            {
                return newModel;
            }

            return new MonsterModel
            {
                Id = newModel.Id,
                Ailments = newModel.Ailments.Where((upd, idx) => !existing.Ailments[idx].Equals(upd)).ToList(),
                Parts = newModel.Parts.Where((upd, idx) => !existing.Parts[idx].Equals(upd)).ToList()
            };
        }

        public void Clear() => this.state.Clear();
    }
}