using System.Globalization;
using FullBleed.DotNet.UI;
using static FullBleed.DotNet.UI.Components;

namespace FullBleed.ComponentInvoice;

public sealed record InvoiceItem(string Description, string Detail, decimal Quantity, decimal Rate)
{
    public decimal Amount => decimal.Round(Quantity * Rate, 2, MidpointRounding.AwayFromZero);
}

public sealed record Invoice(string Reference, string Customer, string Project, string Issued, string Due,
    IReadOnlyList<InvoiceItem> Items, bool Paid = false, string Accent = "#315d9b");

public static class InvoiceDocument
{
    public static Invoice Example { get; } = new("FB-1042", "Maple & Finch", "Autumn launch", "01 October 2026", "31 October 2026",
    [
        new("Brand workshop", "Positioning, priorities and a clear creative direction.", 8, 125),
        new("Digital design", "A cohesive visual system, from layout to launch.", 6, 95),
        new("Studio handoff", "Final refinements, documentation and delivery.", 2, 150),
    ]);

    public static Document Create(Invoice invoice)
    {
        // Capture the data used by both rows and totals before constructing a document.
        var items = invoice.Items.ToArray();
        var total = items.Sum(item => item.Amount);
        var styles = new StyleSheet(BaseStyles)
        {
            [".amount-panel"] = new Style { ["background"] = invoice.Accent },
            [".brand, .invoice-title"] = new Style { ["color"] = invoice.Accent },
        };
        return new Document(
            Header(
                Box(Paragraph("NORTHSTAR / STUDIO").WithClass("brand"), Paragraph("Design with direction.").WithClass("muted")),
                Paragraph(invoice.Paid ? "PAID IN FULL" : "INVOICE").WithClass("status", invoice.Paid ? "paid" : null)
            ).WithClass("masthead"),
            Section(
                Heading(1, "Good work.", LineBreak(), "Clear details.").WithClass("invoice-title"),
                Paragraph("A considered record of the work we made together.").WithClass("intro")
            ).WithClass("introduction"),
            LayoutGrid(
                Field("Prepared for", Strong(invoice.Customer), LineBreak(), invoice.Project),
                Field("Invoice " + invoice.Reference, "Issued · " + invoice.Issued, LineBreak(), "Due · " + invoice.Due)
            ).WithClass("details"),
            Table(
                TableHead(TableRow(
                    TableHeader("THE WORK").WithAttribute("scope", "col").WithStyle("width: 55%"),
                    Column("QTY"), Column("RATE"), Column("AMOUNT"))),
                TableBody(items.Length == 0
                    ? TableRow(TableCell("No billable items.").WithAttribute("colspan", "4").WithClass("empty"))
                    : Fragment(items.Select(ItemRow)))
            ).WithClass("line-items").WithAttribute("aria-label", "Invoice line items"),
            Section(
                Box(Heading(2, "A little clarity.", LineBreak(), "A lot of possibility.").WithClass("thanks"),
                    Paragraph("Thank you for inviting us to help shape your next chapter.").WithClass("muted")),
                Card(
                    Paragraph(invoice.Paid ? "TOTAL PAID · USD" : "TOTAL DUE · USD").WithClass("eyebrow"),
                    Paragraph(Money(total)).WithClass("amount"),
                    Paragraph("Tax · 0% in this fictional sample").WithClass("tax")
                ).WithClass("amount-panel")
            ).WithClass("summary"),
            Section(
                Field(invoice.Paid ? "Payment received" : "Payment details",
                    invoice.Paid ? "Thank you. No payment is due." : $"Please reference {invoice.Reference}. Payment instructions are supplied separately."),
                Field("Questions about the work?", "Write to hello@example.invalid.")
            ).WithClass("payment"),
            Footer(Text("NORTHSTAR STUDIO"), Text("FICTIONAL SAMPLE / " + invoice.Reference)).WithClass("closing")
        )
        { Title = "Invoice " + invoice.Reference, Language = "en-US", Styles = styles };
    }

    public static Element Field(string label, params Node?[] children) => Box(
        Heading(2, label).WithClass("eyebrow"), Paragraph(children)).WithClass("field");

    public static Element ItemRow(InvoiceItem item) => TableRow(
        TableCell(Strong(item.Description), Paragraph(item.Detail).WithClass("item-detail")),
        TableCell(item.Quantity.ToString("0.##", CultureInfo.InvariantCulture)).WithClass("number"),
        TableCell(Money(item.Rate)).WithClass("number"),
        TableCell(Money(item.Amount)).WithClass("number"));

    private static Element Column(string text) => TableHeader(text).WithAttribute("scope", "col").WithClass("number");

    public static string Money(decimal value) => (value < 0 ? "-$" : "$") + decimal.Abs(value).ToString("N2", CultureInfo.InvariantCulture);

    public static StyleSheet BaseStyles { get; } = new()
    {
        ["@page"] = new Style { ["size"] = "A4", ["margin"] = "17mm 18mm" },
        ["*"] = new Style { ["box-sizing"] = "border-box" },
        ["body"] = new Style { ["margin"] = "0", ["font-family"] = "Inter", ["font-size"] = "9pt", ["line-height"] = "1.5", ["color"] = "#263b46" },
        ["h1, h2, p"] = new Style { ["margin"] = "0" },
        [".masthead"] = new Style { ["display"] = "flex", ["justify-content"] = "space-between", ["align-items"] = "center", ["border-bottom"] = "1pt solid #cad3dc", ["padding-bottom"] = "17pt" },
        [".brand"] = new Style { ["font-size"] = "9pt", ["font-weight"] = "700", ["letter-spacing"] = "1.4pt" },
        [".muted"] = new Style { ["color"] = "#667580", ["font-size"] = "8pt" },
        [".status"] = new Style { ["font-size"] = "7pt", ["font-weight"] = "700", ["letter-spacing"] = "1pt", ["border"] = "1pt solid #cad3dc", ["border-radius"] = "14pt", ["padding"] = "5pt 11pt" },
        [".status.paid"] = new Style { ["color"] = "#245541", ["background"] = "#e6f1e9", ["border-color"] = "#b2d2bd" },
        [".introduction"] = new Style { ["padding"] = "25pt 0 23pt" },
        [".invoice-title"] = new Style { ["font-family"] = "DMSerifDisplay-Regular", ["font-weight"] = "400", ["font-size"] = "42pt", ["line-height"] = "1.02", ["letter-spacing"] = "-0.8pt" },
        [".intro"] = new Style { ["margin-top"] = "10pt", ["color"] = "#667580", ["font-size"] = "9pt" },
        [".details"] = new Style { ["grid-template-columns"] = "1.2fr 1fr", ["gap"] = "24pt", ["background"] = "#f2f5f8", ["padding"] = "15pt", ["border-left"] = "3pt solid #bacada", ["margin-bottom"] = "21pt" },
        [".eyebrow"] = new Style { ["font-size"] = "7pt", ["font-weight"] = "700", ["letter-spacing"] = "0.8pt", ["text-transform"] = "uppercase" },
        [".field > p"] = new Style { ["margin-top"] = "5pt", ["font-size"] = "8.5pt" },
        [".line-items"] = new Style { ["font-size"] = "8.5pt" },
        [".line-items th"] = new Style { ["text-align"] = "left", ["font-size"] = "6.8pt", ["letter-spacing"] = "0.7pt", ["color"] = "#667580", ["padding"] = "0 0 9pt", ["border-bottom"] = "1pt solid #7c91a3" },
        [".line-items td"] = new Style { ["padding"] = "12pt 0", ["vertical-align"] = "top", ["border-bottom"] = "0.5pt solid #d7dfe5" },
        [".line-items tr"] = new Style { ["break-inside"] = "avoid" },
        [".line-items .number"] = new Style { ["text-align"] = "right", ["white-space"] = "nowrap" },
        [".item-detail"] = new Style { ["font-size"] = "7.2pt", ["color"] = "#667580", ["margin-top"] = "3pt", ["padding-right"] = "12pt" },
        [".summary"] = new Style { ["display"] = "grid", ["grid-template-columns"] = "1fr 1fr", ["gap"] = "24pt", ["margin-top"] = "24pt", ["break-inside"] = "avoid" },
        [".thanks"] = new Style { ["font-family"] = "DMSerifDisplay-Regular", ["font-weight"] = "400", ["font-size"] = "20pt", ["line-height"] = "1.15", ["margin"] = "5pt 0 10pt" },
        [".amount-panel"] = new Style { ["color"] = "#ffffff", ["border"] = "none", ["border-radius"] = "0", ["padding"] = "16pt" },
        [".amount"] = new Style { ["font-size"] = "28pt", ["font-weight"] = "600", ["letter-spacing"] = "-0.8pt", ["margin"] = "6pt 0" },
        [".tax"] = new Style { ["font-size"] = "6.5pt" },
        [".payment"] = new Style { ["display"] = "grid", ["grid-template-columns"] = "1fr 1fr", ["gap"] = "24pt", ["border-top"] = "0.6pt solid #cad3dc", ["padding-top"] = "14pt", ["margin-top"] = "25pt", ["break-inside"] = "avoid" },
        [".closing"] = new Style { ["display"] = "flex", ["justify-content"] = "space-between", ["border-top"] = "0.6pt solid #cad3dc", ["padding-top"] = "9pt", ["margin-top"] = "24pt", ["font-size"] = "6pt", ["letter-spacing"] = "0.6pt", ["color"] = "#667580" },
    };
}
