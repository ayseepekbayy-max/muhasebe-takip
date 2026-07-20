using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;

    public DetayModel(AppDbContext db)
    {
        _db = db;
    }

    public MaliyetKaydi? Kayit { get; set; }

    public MaliyetKaydiDetay Detay { get; set; } = new();

    public List<MaliyetDolapOzet> Dolaplar { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");

        if (firmaId == null)
            return RedirectToPage("/Login");

        Kayit = await _db.MaliyetKayitlari
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.FirmaId == firmaId.Value);

        if (Kayit == null)
            return RedirectToPage("/Maliyet/Index");

        Detay = DetayOku(Kayit.DetayJson);
        Dolaplar = DolaplariOku(Detay.DolaplarJson);

        return Page();
    }

    private static List<MaliyetDolapOzet> DolaplariOku(string? json)
    {
        var liste = new List<MaliyetDolapOzet>();

        if (string.IsNullOrWhiteSpace(json))
            return liste;

        try
        {
            using var belge = JsonDocument.Parse(json);
            if (belge.RootElement.ValueKind != JsonValueKind.Array)
                return liste;

            foreach (var item in belge.RootElement.EnumerateArray())
            {
                var adet = GetDecimal(item, "quantity");
                var parcaAdedi = 0m;

                if (item.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                        parcaAdedi += GetDecimal(part, "adet") * adet;
                }

                liste.Add(new MaliyetDolapOzet
                {
                    Ad = GetString(item, "name"),
                    Genislik = GetDecimal(item, "width"),
                    Yukseklik = GetDecimal(item, "height"),
                    Derinlik = GetDecimal(item, "depth"),
                    Adet = adet,
                    ParcaAdedi = parcaAdedi
                });
            }
        }
        catch
        {
            return new List<MaliyetDolapOzet>();
        }

        return liste;
    }

    private static string GetString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static decimal GetDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result))
            return result;

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out result))
            return result;

        return 0;
    }

    private static MaliyetKaydiDetay DetayOku(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MaliyetKaydiDetay();

        try
        {
            return JsonSerializer.Deserialize<MaliyetKaydiDetay>(json) ?? new MaliyetKaydiDetay();
        }
        catch
        {
            return new MaliyetKaydiDetay();
        }
    }
}


public class MaliyetDolapOzet
{
    public string Ad { get; set; } = "";

    public decimal Genislik { get; set; }

    public decimal Yukseklik { get; set; }

    public decimal Derinlik { get; set; }

    public decimal Adet { get; set; }

    public decimal ParcaAdedi { get; set; }
}
