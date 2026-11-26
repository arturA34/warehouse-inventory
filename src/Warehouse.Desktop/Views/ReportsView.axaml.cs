using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Warehouse.Application;
using Warehouse.Desktop.ViewModels;

namespace Warehouse.Desktop.Views;

public sealed partial class ReportsView : UserControl
{
    private ReportsViewModel? model;

    public ReportsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (model is not null) model.PropertyChanged -= OnReportChanged;
        model = DataContext as ReportsViewModel;
        if (model is not null) model.PropertyChanged += OnReportChanged;
        UpdatePreview();
    }

    private void OnReportChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ReportsViewModel.Table)) UpdatePreview();
    }

    private void UpdatePreview()
    {
        Preview.Columns.Clear();
        if (model?.Table is not { } table)
        {
            Preview.ItemsSource = null;
            return;
        }

        var numeric = table.NumericColumns.ToHashSet();
        for (var index = 0; index < table.Columns.Count; index++)
        {
            Preview.Columns.Add(new DataGridTextColumn
            {
                Header = table.Columns[index],
                Binding = new Binding($"[{index}]"),
                Width = new DataGridLength(numeric.Contains(index) ? 145 : 225),
                CanUserSort = false
            });
        }
        Preview.ItemsSource = table.Rows;
    }

    private async void OnGenerate(object? sender, RoutedEventArgs args)
    {
        if (model is not null) await model.GenerateAsync();
    }

    private async void OnExcel(object? sender, RoutedEventArgs args)
    {
        if (model is not null) await model.SaveAsync(ExportFormat.Excel, SaveFileAsync);
    }

    private async Task<bool> SaveFileAsync(ReportFile file)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null || !provider.CanSave)
            throw new IOException("Сохранение файлов недоступно.");
        var extension = Path.GetExtension(file.Name);
        var output = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить отчёт",
            SuggestedFileName = file.Name,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(extension.TrimStart('.').ToUpperInvariant())
                { Patterns = ["*" + extension], MimeTypes = [file.ContentType] }]
        });
        if (output is null) return false;

        await using var stream = await output.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await stream.WriteAsync(file.Content);
        return true;
    }
}
