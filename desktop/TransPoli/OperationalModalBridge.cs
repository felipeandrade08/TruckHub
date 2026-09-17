using System.Windows;
using System.Windows.Input;

namespace TransPoli;

public partial class MainWindow
{
    internal void OpenOperationalModalFromShortcut(string kind)
    {
        ShowOperationalModal(kind);
    }
}

// F10 is intentionally reserved exclusively for opening/closing the tablet.
// Operational features are opened only through the tablet UI.
