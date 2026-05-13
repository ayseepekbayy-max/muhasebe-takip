using Microsoft.AspNetCore.Http;

namespace MuhasebeTakip2.App.Services.Ai;

public class ConversationMemoryService
{
    private readonly IHttpContextAccessor _http;

    public ConversationMemoryService(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void SonCalisaniKaydet(string ad)
    {
        _http.HttpContext?.Session.SetString("AiSonCalisan", ad);
    }

    public string? SonCalisaniGetir()
    {
        return _http.HttpContext?.Session.GetString("AiSonCalisan");
    }

    public void SonKonuyuKaydet(string konu)
    {
        _http.HttpContext?.Session.SetString("AiSonKonu", konu);
    }

    public string? SonKonuyuGetir()
    {
        return _http.HttpContext?.Session.GetString("AiSonKonu");
    }
}