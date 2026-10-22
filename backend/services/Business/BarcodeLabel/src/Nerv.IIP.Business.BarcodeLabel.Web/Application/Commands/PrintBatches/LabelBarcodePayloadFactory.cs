using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

internal static class LabelBarcodePayloadFactory
{
    public static LabelBarcodePayload Create(string barcodeType, string labelValue) =>
        barcodeType switch
        {
            "code128" => new PlainLabelBarcodePayload(PlainLabelBarcodeType.Code128, labelValue),
            "qr" => new PlainLabelBarcodePayload(PlainLabelBarcodeType.Qr, labelValue),
            "datamatrix" => new PlainLabelBarcodePayload(PlainLabelBarcodeType.DataMatrix, labelValue),
            "gs1-128" => new Gs1LabelBarcodePayload(
                Gs1LabelBarcodeType.Gs1128,
                Gs1ApplicationIdentifierParser.Parse(labelValue)),
            "gs1-datamatrix" => new Gs1LabelBarcodePayload(
                Gs1LabelBarcodeType.DataMatrix,
                Gs1ApplicationIdentifierParser.Parse(labelValue)),
            _ => throw new InvalidOperationException($"Unsupported barcode type '{barcodeType}'."),
        };
}
