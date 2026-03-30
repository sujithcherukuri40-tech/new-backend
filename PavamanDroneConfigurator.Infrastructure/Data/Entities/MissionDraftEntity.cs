using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PavamanDroneConfigurator.Infrastructure.Data.Entities;

[Table("mission_drafts")]
public class MissionDraftEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string JsonBlob { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
}
