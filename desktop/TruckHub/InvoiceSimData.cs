using System;
using System.Linq;
using System.Text;

namespace TruckHub;

/// <summary>
/// Dados cadastrais SIMULADOS para a nota da carga. Tudo é derivado de forma
/// determinística do nome da empresa (mesma empresa = mesmo CNPJ/endereço sempre),
/// com dígitos verificadores válidos, para a DANFE parecer completa sem
/// inventar nada que pareça real: o documento continua marcado como simulação.
/// </summary>
internal static class InvoiceSimData
{
    public const decimal IcmsRate = 12m;   // alíquota interestadual de referência

    private static readonly string[] Streets =
    {
        "RODOVIA BR-116 KM", "AV. DOS TRANSPORTADORES", "RUA DO COMERCIO", "AV. INDUSTRIAL",
        "ESTRADA VELHA KM", "RUA DOS CAMINHONEIROS", "AV. DO PORTO", "RODOVIA DOS IMIGRANTES KM",
        "ALAMEDA LOGISTICA", "RUA DA FABRICA"
    };
    private static readonly string[] Districts =
    {
        "DISTRITO INDUSTRIAL", "CENTRO", "ZONA PORTUARIA", "PARQUE LOGISTICO", "VILA NOVA", "JARDIM DAS ROTAS"
    };

    public sealed record Party(string Cnpj, string Ie, string Address, string District, string Cep, string Phone, string Uf);

    private static int Seed(string text)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in (text ?? "").ToUpperInvariant()) h = h * 31 + c;
            return h & 0x7FFFFFFF;
        }
    }

    public static Party For(string name)
    {
        var rnd = new Random(Seed(name));
        var d = new int[14];
        for (var i = 0; i < 8; i++) d[i] = rnd.Next(0, 10);
        d[8] = 0; d[9] = 0; d[10] = 0; d[11] = 1;
        d[12] = Dv(d, 12, new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        d[13] = Dv(d, 13, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        var s = string.Concat(d);
        var cnpj = $"{s[..2]}.{s[2..5]}.{s[5..8]}/{s[8..12]}-{s[12..]}";
        var ie = string.Concat(Enumerable.Range(0, 9).Select(_ => rnd.Next(0, 10)));
        var street = $"{Streets[rnd.Next(Streets.Length)]} {rnd.Next(1, 999)}";
        var district = Districts[rnd.Next(Districts.Length)];
        var cep = $"{rnd.Next(10000, 99999)}-{rnd.Next(100, 999)}";
        var phone = $"(11) 4{rnd.Next(100, 999)}-{rnd.Next(1000, 9999)}";
        return new Party(cnpj, ie, street, district, cep, phone, "EX");
    }

    public static string Protocol(string key)
    {
        var rnd = new Random(Seed(key));
        return "1" + string.Concat(Enumerable.Range(0, 14).Select(_ => rnd.Next(0, 10)));
    }

    public static string Antt(string plate)
        => $"ANTT {Math.Abs(Seed(plate)) % 90000000 + 10000000}";

    private static int Dv(int[] d, int len, int[] w)
    {
        var sum = 0;
        for (var i = 0; i < len; i++) sum += d[i] * w[i];
        var r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }
}
