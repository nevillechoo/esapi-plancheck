using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public class PTVCoverageCheck : PlanCheckBase
    {
        public override string Title => "PTV Coverage (95%)";
        public override string Category => "Dose Distribution";
        public override string Description => "Verifies that 95% of the target volume receives at least 95% of the prescribed dose. Limitation: Searches for the first structure starting with 'PTV'. Does not check multiple PTVs.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ptv = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.StartsWith("PTV", StringComparison.OrdinalIgnoreCase));
            if (ptv == null)
                return NA("No PTV found.");
            var dvh = plan.GetDVHCumulativeData(ptv, DoseValuePresentation.Relative, VolumePresentation.Relative, 0.1);
            if (dvh == null)
                return Fail("Could not retrieve DVH for PTV.");
            var v95 = dvh.CurveData.FirstOrDefault(p => Math.Abs(p.DoseValue.Dose - 95.0) < 0.1);
            
            double volumeAt95 = plan.GetVolumeAtDose(ptv, new DoseValue(95.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative);

            if (volumeAt95 < 95.0)
            {
                return Fail($"PTV Coverage is {volumeAt95:F1}% (Goal: >95%)");
            }

            return Pass($"PTV Coverage: {volumeAt95:F1}%");
        }
    }

    public class HotspotCheck : PlanCheckBase
    {
        public override string Title => "Global Max Dose";
        public override string Category => "Dose Distribution";
        public override string Description => "Verifies the global maximum dose is < 107% of the prescription. Limitation: Analyzes the 3D dose grid; does not differentiate between clinically relevant and irrelevant hotspots.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var maxDose = plan.Dose.DoseMax3D;
            double prescribedDose = plan.TotalDose.Dose;
            
            if (prescribedDose <= 0) return NA("Cannot calculate relative max dose (no Rx).");

            double maxDoseRel;
            double maxDoseGy;

            if (maxDose.Unit == DoseValue.DoseUnit.Percent)
            {
                maxDoseRel = maxDose.Dose;
                maxDoseGy = (maxDoseRel / 100.0) * prescribedDose;
            }
            else
            {
                maxDoseGy = maxDose.Dose;
                if (maxDose.Unit == DoseValue.DoseUnit.cGy) maxDoseGy /= 100.0;
                maxDoseRel = (maxDoseGy / prescribedDose) * 100.0;
            }

            string summary = $"{maxDoseGy:F2} Gy ({maxDoseRel:F1}%)";
            string details = $"Prescribed Dose: {prescribedDose:F2} Gy, Global Max: {maxDoseGy:F2} Gy";

            if (maxDoseRel > 107.0)
            {
                return Warning($"High Hotspot: {summary} (>107%)", details);
            }

            return Pass($"Global Max: {summary}", details);
        }
    }

    public class DoseAlgorithmCheck : PlanCheckBase
    {
        public override string Title => "Dose Algorithm";
        public override string Category => "Dose Distribution";
        public override string Description => "Displays the calculation algorithm (e.g., AAA, Acuros). Limitation: Checks PhotonCalculationModel first; falls back to Electron model if photon algorithm is not found.";

        public override CheckResult Execute(PlanSetup plan)
        {
            string algo = plan.PhotonCalculationModel;
            if (string.IsNullOrEmpty(algo) && plan.Beams.Any(b => !b.IsSetupField && b.EnergyModeDisplayName.Contains("E")))
            {
                 algo = plan.ElectronCalculationModel;
            }

            if (string.IsNullOrEmpty(algo))
            {
                return Warning("Unknown or not set Dose Algorithm.");
            }

            return Pass($"Algorithm: {algo}");
        }
    }
}
