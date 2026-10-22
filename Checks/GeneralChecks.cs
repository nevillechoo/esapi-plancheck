using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public class PatientIdentityCheck : PlanCheckBase
    {
        public override string Title => "Patient Verification";
        public override string Category => "General";
        public override string Description => "Verifies that patient demographic data is present in Eclipse. Limitation: Does not cross-verify with external R&V systems or ID cards.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var patient = plan.Course.Patient;
            if (string.IsNullOrEmpty(patient.Id) || string.IsNullOrEmpty(patient.LastName))
            {
                return Fail("Patient ID or Name is missing.");
            }
            return Pass($"Patient: {patient.LastName}, {patient.FirstName} ({patient.Id})");
        }
    }

    public class PlanParametersCheck : PlanCheckBase
    {
        public override string Title => "Plan Parameters";
        public override string Category => "General";
        public override string Description => "Checks for basic plan integrity (ID, Course, Machine, Energy). Limitation: Only verifies presence of data; does not evaluate appropriateness of machine or energy choice.";

        public override CheckResult Execute(PlanSetup plan)
        {
            if (string.IsNullOrEmpty(plan.Id))
                return Fail("Plan ID is missing.");

            var machines = new HashSet<string>();
            var energies = new HashSet<string>();

            foreach (var beam in plan.Beams)
            {
                if (beam.IsSetupField) continue;

                if (beam.TreatmentUnit != null)
                    machines.Add(beam.TreatmentUnit.Id);

                if (!string.IsNullOrEmpty(beam.EnergyModeDisplayName))
                    energies.Add(beam.EnergyModeDisplayName);
                else
                    return Warning($"Beam {beam.Id} has no energy mode defined.");
            }

            string machineStr = machines.Count > 0 ? string.Join(", ", machines) : "Unknown";
            string energyStr = energies.Count > 0 ? string.Join(", ", energies) : "Unknown";

            return Pass($"Plan: {plan.Id}, Machine: {machineStr}, Energy: {energyStr}");
        }
    }

    public class PrescriptionCheck : PlanCheckBase
    {
        public override string Title => "Prescription Verification";
        public override string Category => "General";
        public override string Description => "Ensures Rx dose and fractionation are defined. Limitation: Does not verify if the values match the physics directive or MD intent.";

        public override CheckResult Execute(PlanSetup plan)
        {
            if (plan.TotalDose == null || plan.TotalDose.Dose == 0)
            {
                return Fail("Total prescribed dose is 0 or missing.");
            }

            if (plan.NumberOfFractions == null || plan.NumberOfFractions == 0)
            {
                return Fail("Number of fractions is 0 or missing.");
            }

            return Pass($"Dose: {plan.TotalDose} in {plan.NumberOfFractions} fx");
        }
    }
}
