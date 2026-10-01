using System.Text;
using AccessWifiService;
using Models.DataBase;

namespace AccessWifi.Api.Tests;

public class LeadsCsvTests
{
    [Fact]
    public void Build_InstagramSaiComoLinkDoPerfil_EOForaDoFormatoComoFoiDigitado()
    {
        LeadReportRow[] arrRows =
        [
            new LeadReportRow(new Lead { Nome = "Novo", Instagram = "https://www.instagram.com/ana.souza" }, "Matriz"),
            new LeadReportRow(new Lead { Nome = "Antigo", Instagram = "@itsthejordan_" }, "Matriz"),
            new LeadReportRow(new Lead { Nome = "Email", Instagram = "biell6555@gmail.com" }, "Matriz"),
            new LeadReportRow(new Lead { Nome = "Vazio", Instagram = "" }, "Matriz"),
        ];

        string[] arrLinhas = Encoding.UTF8.GetString(LeadsCsv.Build(arrRows)).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(sLinha => sLinha.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        Assert.Equal("https://www.instagram.com/ana.souza", arrLinhas[1].Split(',')[3]);
        Assert.Equal("https://www.instagram.com/itsthejordan_", arrLinhas[2].Split(',')[3]);
        Assert.Equal("biell6555@gmail.com", arrLinhas[3].Split(',')[3]);
        Assert.Equal("", arrLinhas[4].Split(',')[3]);
    }
}
