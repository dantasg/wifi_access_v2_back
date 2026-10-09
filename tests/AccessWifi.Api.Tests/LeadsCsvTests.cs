using System.Text;
using Models.Reports;
using Models.DataBase;

namespace AccessWifi.Api.Tests;

public class LeadsCsvTests
{
    [Fact]
    public void Build_InstagramBecomesProfileLink_AndInvalidOneAsTyped()
    {
        LeadReportRow[] arrRows =
        [
            new LeadReportRow(new Lead { Name = "Novo", Instagram = "https://www.instagram.com/ana.souza" }, "Matriz"),
            new LeadReportRow(new Lead { Name = "Antigo", Instagram = "@itsthejordan_" }, "Matriz"),
            new LeadReportRow(new Lead { Name = "Email", Instagram = "biell6555@gmail.com" }, "Matriz"),
            new LeadReportRow(new Lead { Name = "Vazio", Instagram = "" }, "Matriz"),
        ];

        string[] arrLines = Encoding.UTF8.GetString(LeadsCsv.Build(arrRows)).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(sLine => sLine.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        Assert.Equal("https://www.instagram.com/ana.souza", arrLines[1].Split(',')[3]);
        Assert.Equal("https://www.instagram.com/itsthejordan_", arrLines[2].Split(',')[3]);
        Assert.Equal("biell6555@gmail.com", arrLines[3].Split(',')[3]);
        Assert.Equal("", arrLines[4].Split(',')[3]);
    }
}
