using System.ComponentModel.DataAnnotations;

namespace SchoolManagement.Models;

public class SetUserActiveViewModel
{
    [Required]
    [StringLength(450)]
    public string Id { get; set; } = string.Empty;

    [Required]
    public bool? Active { get; set; }

    [Required]
    [MinLength(8)]
    [MaxLength(8)]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
