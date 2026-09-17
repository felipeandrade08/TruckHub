using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    /// <summary>
    /// FASE C: tela de confirmação de abastecimento.
    /// A telemetria fornece os litros; o motorista informa somente preço/L e posto.
    /// A cidade é derivada da telemetria e não é digitada.
    /// </summary>
    internal void ShowFuelPaymentModalC()
    {
        var telemetry = _pendingRefuelTelemetry;
        var liters = _pendingRefuelLiters;
        if (telemetry is null || liters <= 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(ModalPanel(new TextBlock
            {
                Text = "⛽ Nenhum abastecimento detectado. O TruckHub acompanha o tanque automaticamente e abrirá esta confirmação quando identificar litros adicionados.",
                FontSize = 13,
                Foreground = FindResource("Text") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
            ShowModalContent("fuel-c", BuildModalCard("⛽ ABASTECIMENTO", empty, "Aguardando detecção pela telemetria"));
            return;
        }

        var panel = new StackPanel();
        panel.Children.Add(ModalPanel(new TextBlock
        {
            Text = $"⛽ ABASTECIMENTO DETECTADO\n{liters:0.0} L adicionados ao tanque. Os litros vieram automaticamente da telemetria.",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap
        }));
        panel.Children.Add(ModalValueRow("Litros detectados", $"{liters:0.0} L", "Green"));
        panel.Children.Add(ModalValueRow("Odômetro", $"{telemetry.OdometerKm:0.0} km"));
        panel.Children.Add(ModalValueRow("Caminhão", $"{telemetry.TruckBrand} {telemetry.TruckModel}".Trim()));
        panel.Children.Add(ModalValueRow("Placa", string.IsNullOrWhiteSpace(telemetry.LicensePlate) ? "Não informada" : telemetry.LicensePlate));

        panel.Children.Add(ModalLabel("PREÇO POR LITRO (R$)"));
        var price = NewV13TextBox("Ex.: 6,19");
        panel.Children.Add(price);

        panel.Children.Add(ModalLabel("POSTO"));
        var station = NewV13TextBox("Nome do posto");
        panel.Children.Add(station);

        var city = telemetry.DestinationCity ?? telemetry.SourceCity ?? "";
        panel.Children.Add(ModalLine(
            string.IsNullOrWhiteSpace(city)
                ? "Localização: não informada pela telemetria"
                : $"Localização automática: {city}", 11));

        var totalText = new TextBlock
        {
            Text = $"Total: {liters:0.0} L × preço por litro",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            Margin = new Thickness(0, 10, 0, 10)
        };
        panel.Children.Add(totalText);

        price.TextChanged += (_, _) =>
        {
            if (TryMoney(price.Text, out var value) && value > 0)
                totalText.Text = $"Total: R$ {(decimal)liters * value:0.00}";
            else
                totalText.Text = $"Total: {liters:0.0} L × preço por litro";
        };

        var save = ModalButton("✓ CONFIRMAR ABASTECIMENTO");
        save.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!TryMoney(price.Text, out var priceValue) || priceValue <= 0)
            {
                StatusText.Text = "TransPoli • informe o preço por litro";
                return;
            }
            if (string.IsNullOrWhiteSpace(station.Text))
            {
                StatusText.Text = "TransPoli • informe o posto";
                return;
            }

            save.IsEnabled = false;
            var location = string.IsNullOrWhiteSpace(city) ? "Não informado" : city;
            await RegisterFuelPaymentV13Async(telemetry, liters, priceValue, station.Text.Trim(), location);
        };
        panel.Children.Add(save);

        var cancel = ModalButton("✕ CANCELAR");
        cancel.Click += (_, e) =>
        {
            e.Handled = true;
            _pendingRefuelTelemetry = null;
            _pendingRefuelLiters = 0;
            CloseOperationalModal();
        };
        panel.Children.Add(cancel);

        ShowModalContent("fuel-c", BuildModalCard("⛽ ABASTECIMENTO", panel, "Litros detectados automaticamente pela telemetria"));
    }
}
