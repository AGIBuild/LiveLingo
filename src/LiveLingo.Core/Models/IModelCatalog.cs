namespace LiveLingo.Core.Models;

public interface IModelCatalog
{
    IReadOnlyList<ModelProfile> AllProfiles { get; }

    IReadOnlyList<ModelProfile> GetProfiles(ModelTaskType taskType);

    ModelProfile? FindById(string id);
}
