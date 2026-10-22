using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EsapiPlanCheck.ViewModels;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                var vm = this.DataContext as MainViewModel;
                if (vm == null)
                {
                    MessageBox.Show("No data to print.");
                    return;
                }

                FlowDocument doc = new FlowDocument();
                doc.PagePadding = new Thickness(20);
                doc.FontFamily = new FontFamily("Verdana"); 
                doc.FontSize = 10;
                doc.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
                
                Typography.SetStandardLigatures(doc, false);
                Typography.SetContextualAlternates(doc, false);
                Typography.SetDiscretionaryLigatures(doc, false);
                Typography.SetHistoricalLigatures(doc, false);
                doc.IsOptimalParagraphEnabled = false;
                doc.IsHyphenationEnabled = false;
                doc.TextAlignment = TextAlignment.Left;
                TextOptions.SetTextFormattingMode(doc, TextFormattingMode.Display); 
                
                doc.PageWidth = printDialog.PrintableAreaWidth;
                doc.PageHeight = printDialog.PrintableAreaHeight;
                doc.ColumnWidth = double.PositiveInfinity; 

                Paragraph title = new Paragraph(new Run("ESAPI Plan Check Report"));
                title.FontSize = 18;
                title.FontWeight = FontWeights.Bold;
                title.TextAlignment = TextAlignment.Center;
                title.Margin = new Thickness(0, 0, 0, 20);
                doc.Blocks.Add(title);

                
                Paragraph info = new Paragraph(new Run($"Date: {DateTime.Now}"));
                info.TextAlignment = TextAlignment.Right;
                doc.Blocks.Add(info);

                
                Table table = new Table();
                table.CellSpacing = 0;
                table.BorderBrush = Brushes.Gray;
                table.BorderThickness = new Thickness(1);

             
                table.Columns.Add(new TableColumn() { Width = new GridLength(1.5, GridUnitType.Star) });
                table.Columns.Add(new TableColumn() { Width = new GridLength(2, GridUnitType.Star) });
                table.Columns.Add(new TableColumn() { Width = new GridLength(3, GridUnitType.Star) });
                table.Columns.Add(new TableColumn() { Width = new GridLength(1.2, GridUnitType.Star) });
                table.Columns.Add(new TableColumn() { Width = new GridLength(3.5, GridUnitType.Star) });
                table.Columns.Add(new TableColumn() { Width = new GridLength(10, GridUnitType.Star) });
                table.Columns.Add(new TableColumn() { Width = new GridLength(2.5, GridUnitType.Star) });

              
                TableRowGroup headerGroup = new TableRowGroup();
                TableRow headerRow = new TableRow();
                headerRow.Background = Brushes.LightGray;
                headerRow.FontWeight = FontWeights.Bold;
                headerRow.Cells.Add(CreateHeaderCell("Category"));
                headerRow.Cells.Add(CreateHeaderCell("Check"));
                headerRow.Cells.Add(CreateHeaderCell("Description"));
                headerRow.Cells.Add(CreateHeaderCell("Status"));
                headerRow.Cells.Add(CreateHeaderCell("Message"));
                headerRow.Cells.Add(CreateHeaderCell("Details"));
                headerRow.Cells.Add(CreateHeaderCell("Comments"));
                headerGroup.Rows.Add(headerRow);
                table.RowGroups.Add(headerGroup);

                
                TableRowGroup dataGroup = new TableRowGroup();
                foreach (var result in vm.Results)
                {
                    TableRow row = new TableRow();
                    row.Cells.Add(CreateTextCell(result.Category));
                    row.Cells.Add(CreateTextCell(result.Title));
                    row.Cells.Add(CreateTextCell(result.Description));
                    
                  
                    TableCell statusCell = CreateTextCell(result.Status.ToString());
                    statusCell.Foreground = GetStatusBrush(result.Status);
                    statusCell.FontWeight = FontWeights.Bold;
                    row.Cells.Add(statusCell);

                    row.Cells.Add(CreateTextCell(result.Message));
                    row.Cells.Add(CreateTextCell(result.Details));
                    row.Cells.Add(CreateTextCell(result.Comments));

                    dataGroup.Rows.Add(row);
                }
                table.RowGroups.Add(dataGroup);

                doc.Blocks.Add(table);

               
                IDocumentPaginatorSource idpSource = doc;
                printDialog.PrintDocument(idpSource.DocumentPaginator, "ESAPI Plan Check Report");
            }
        }

        private TableCell CreateHeaderCell(string text)
        {
            Paragraph p = new Paragraph(new Run(text));
            p.FontFamily = new FontFamily("Verdana");
            p.FontSize = 10;
            p.FontWeight = FontWeights.Bold;

            return new TableCell(p)
            {
                Padding = new Thickness(5),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0, 0, 1, 1)
            };
        }

        private TableCell CreateTextCell(string text)
        {
            Paragraph p = new Paragraph(new Run(text ?? ""));
            p.FontFamily = new FontFamily("Verdana");
            p.FontSize = 10;

            return new TableCell(p)
            {
                Padding = new Thickness(5),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 1, 1)
            };
        }

        private Brush GetStatusBrush(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Pass: return Brushes.Green;
                case CheckStatus.Warning: return Brushes.Orange;
                case CheckStatus.Fail: return Brushes.Red;
                default: return Brushes.Black;
            }
        }
    }
}
