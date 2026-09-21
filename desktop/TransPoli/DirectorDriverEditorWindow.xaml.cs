using System.Windows;
using System.Windows.Controls;

namespace TransPoli;

public partial class DirectorDriverEditorWindow : Window
{
    public string DriverName => NameBox.Text.Trim();
    public string Email => EmailBox.Text.Trim();
    public string Password => PasswordBox.Password;
    public string Pin => PinBox.Password.Trim();
    public string LicenseStatus => (LicenseBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "active";

    public DirectorDriverEditorWindow(string? name,string? email,string? licenseStatus,bool edit)
    {
        InitializeComponent();
        TitleText.Text = edit ? "Editar motorista" : "Novo motorista";
        NameBox.Text=name??"";
        EmailBox.Text=email??"";
        if(!string.IsNullOrWhiteSpace(licenseStatus))
            foreach(var item in LicenseBox.Items)
                if(item is ComboBoxItem cb && cb.Tag?.ToString()==licenseStatus){LicenseBox.SelectedItem=cb;break;}
        if(LicenseBox.SelectedItem==null) LicenseBox.SelectedIndex=0;
        PasswordHint.Text = edit
            ? "Deixe senha e PIN vazios para manter os atuais. Preencha apenas se quiser alterá-los."
            : "Obrigatória no cadastro. A senha será usada no acesso normal do motorista.";
    }

    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(DriverName.Length<2){MessageBox.Show("Informe o nome do motorista.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(!System.Text.RegularExpressions.Regex.IsMatch(Email,@"^\S+@\S+\.\S+$")){MessageBox.Show("Informe um e-mail válido.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(Password.Length>0 && Password.Length<8){MessageBox.Show("A senha deve ter pelo menos 8 caracteres.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(!string.IsNullOrEmpty(Pin) && !System.Text.RegularExpressions.Regex.IsMatch(Pin,@"^\d{6}$")){MessageBox.Show("O PIN deve ter 6 dígitos.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        DialogResult=true; Close();
    }
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
}