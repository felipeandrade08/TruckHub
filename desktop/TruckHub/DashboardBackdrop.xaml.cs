using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TruckHub;

/// <summary>Fundo TruckHub compartilhado por tablet, celular e login. Corta nos cantos arredondados do contêiner.</summary>
public partial class DashboardBackdrop : UserControl
{
    private double _radius;

    public DashboardBackdrop()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateClip();
    }

    /// <summary>Raio dos cantos do contêiner; evita que o fundo "passe" por fora da moldura arredondada.</summary>
    public double CornerRadiusValue
    {
        get => _radius;
        set { _radius = value; UpdateClip(); }
    }

    /// <summary>Lado da imagem que permanece visível quando ela é cortada (Right mostra o caminhão em telas estreitas).</summary>
    public AlignmentX SceneAlignment
    {
        get => SceneBrush.AlignmentX;
        set => SceneBrush.AlignmentX = value;
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), _radius, _radius);
    }
}
