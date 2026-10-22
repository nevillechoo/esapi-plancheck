using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public class DoseGridCheck : PlanCheckBase
    {
        public override string Title => "Dose Grid Resolution";
        public override string Category => "Final Checks";
        public override string Description => "Verifies calculation grid size (recommended <= 2.5mm for VMAT). Limitation: Requires calculated dose to be present.";

        public override CheckResult Execute(PlanSetup plan)
        {
            if (plan.Dose == null)
            {
                return NA("Dose not calculated. Cannot verify grid resolution.");
            }

            double xRes = plan.Dose.XRes;
            double yRes = plan.Dose.YRes;
            double zRes = plan.Dose.ZRes;

            string resMsg = $"{xRes:F1} x {yRes:F1} x {zRes:F1} mm";
            bool isVmat = plan.Beams.Any(b => b.Technique.Id.Contains("Arc"));
            if (isVmat && (xRes > 2.51 || yRes > 2.51 || zRes > 2.51))
            {
                return Warning($"Dose grid resolution ({resMsg}) is coarse for VMAT. Recommended <= 2.5mm.");
            }

            return Pass($"Dose Grid Resolution: {resMsg}");
        }
    }

    public class HeterogeneityCheck : PlanCheckBase
    {
        public override string Title => "Heterogeneity Correction";
        public override string Category => "Final Checks";
        public override string Description => "Reminds physicist to verify heterogeneity correction is enabled. Limitation: Direct API access to this setting varies; manual verification is the primary method.";

        public override CheckResult Execute(PlanSetup plan)
        {
            return Pass("Ensure Heterogeneity Correction is ON (Manual Verification recommended).");
        }
    }
}
