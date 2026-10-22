using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiPlanCheck.Views;
using EsapiPlanCheck.ViewModels;

namespace VMS.TPS
{
    public class Script
    {
        public Script()
        {
        }

        public void Execute(ScriptContext context )
        {
            
            if (context.PlanSetup == null)
            {
                MessageBox.Show("Please open a plan before running this script.");
                return;
            }

            try
            {
                var window = new Window();
                var viewModel = new EsapiPlanCheck.ViewModels.MainViewModel(context.PlanSetup);
                var view = new EsapiPlanCheck.Views.MainView { DataContext = viewModel };
                
                window.Content = view;
                window.Title = $"ESAPI Plan Check - {context.Patient.Id}";
                window.Width = 800;
                window.Height = 600;
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                
                window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}\n\n{ex.StackTrace}");
            }
        }
    }
}
