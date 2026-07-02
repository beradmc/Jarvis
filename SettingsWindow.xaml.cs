using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JarvisCSharp.Config;

namespace JarvisCSharp
{
    public partial class SettingsWindow : Window
    {
        private AppConfig _config;

        public SettingsWindow()
        {
            InitializeComponent();
            _config = AppConfig.Load();
            LoadSettings();
        }

        private void LoadSettings()
        {
            SandboxToggle.IsChecked = _config.Automation.SandboxMode;
            
            // Confirmation Level
            foreach (ComboBoxItem item in ConfirmationLevelCombo.Items)
            {
                if (item.Tag.ToString() == _config.Automation.ConfirmationLevel)
                {
                    ConfirmationLevelCombo.SelectedItem = item;
                    break;
                }
            }
            if (ConfirmationLevelCombo.SelectedItem == null)
                ConfirmationLevelCombo.SelectedIndex = 1; // Default to medium

            SpeedSlider.Value = _config.Automation.AutomationSpeed;
            TimeoutText.Text = _config.Automation.TimeoutsMs.ToString();
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedValueText != null)
            {
                SpeedValueText.Text = $"{e.NewValue:0.0}x";
            }
        }

        private void TimeoutText_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow numbers
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _config.Automation.SandboxMode = SandboxToggle.IsChecked ?? false;

            if (ConfirmationLevelCombo.SelectedItem is ComboBoxItem item)
            {
                _config.Automation.ConfirmationLevel = item.Tag?.ToString() ?? "medium";
            }

            _config.Automation.AutomationSpeed = SpeedSlider.Value;

            if (int.TryParse(TimeoutText.Text, out int timeouts))
            {
                _config.Automation.TimeoutsMs = timeouts;
            }

            _config.Save();
            
            // Reload config globally just in case (though Save updates the cache)
            AppConfig.Load();
            
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
