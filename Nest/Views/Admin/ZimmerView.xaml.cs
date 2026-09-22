using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nest.Views.Admin
{
    /// <summary>
    /// Interaktionslogik für ZimmerView.xaml
    /// </summary>
    public partial class ZimmerView : UserControl
    {
        public ZimmerView()
        {
            InitializeComponent();
        }

        // Nur zahlen
        private void NumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void PricePreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                e.Handled = true;
                return;
            }

            string newText = textBox.Text.Insert(
                textBox.SelectionStart,
                e.Text);

            // Ersetze Punkt durch Komma
            newText = newText.Replace('.', ',');


            // Überprüfe, ob der Text ein gültiges Dezimalformat hat
            if (!decimal.TryParse(
                    newText,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.GetCultureInfo("de-DE"),
                    out decimal value))
            {
                e.Handled = true;
                return;
            }


            // Maximales Price
            if (value > 9999.99m)
            {
                e.Handled = true;
                return;
            }


            // Überprüfe, ob der Text mehr als zwei Nachkommastellen hat
            if (newText.Contains(','))
            {
                string decimalPart = newText.Split(',')[1];

                if (decimalPart.Length > 2)
                {
                    e.Handled = true;
                    return;
                }
            }


            e.Handled = false;
        }
    }
}
