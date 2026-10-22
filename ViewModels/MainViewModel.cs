using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VMS.TPS.Common.Model.API;
using EsapiPlanCheck.Checks;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.ViewModels
{
    public class MainViewModel
    {
        public ObservableCollection<CheckResult> Results { get; set; }
        private PlanSetup _plan;

        public MainViewModel(PlanSetup plan)
        {
            _plan = plan;
            Results = new ObservableCollection<CheckResult>();
            RunChecks();
        }

        private void RunChecks()
        {
            Results.Clear();
            var checks = new List<PlanCheckBase>
            {
                new PatientIdentityCheck(),
                new PlanParametersCheck(),
                new PrescriptionCheck(),
                new SliceThicknessCheck(),
                new UserOriginCheck(),
                new CTDateAcquiredCheck(),
                new CouchStructureCheck(),
                new StructureSetCheck(),
                new NamingConventionCheck(),
                new ContourContinuityCheck(),
                new ContourContiguityCheck(),
                new ContourIslandsCheck(),
                new ContourOverlapCheck(),
                new TargetHierarchyCheck(),
                new SmallTargetCheck(),
                new PTVCoverageCheck(),
                new HotspotCheck(),
                new DoseAlgorithmCheck(),
                new JawSettingCheck(),
                new VMATCollimatorCheck(),
                new CollimatorAngleDiversityCheck(),
                new MUCheck(),
                new VMATApertureCheck(),
                new StaticFieldSizeCheck(),
                new DoseGridCheck(),
                new HeterogeneityCheck()
            };

            foreach (var check in checks)
            {
                try
                {
                    var result = check.Execute(_plan);
                    Results.Add(result);
                }
                catch (Exception ex)
                {
                    Results.Add(new CheckResult(check.Title, check.Category, CheckStatus.Fail, "Error executing check", ex.Message));
                }
            }
        }
    }
}
