using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public class SliceThicknessCheck : PlanCheckBase
    {
        public override string Title => "CT Slice Thickness";
        public override string Category => "CT Image";
        public override string Description => "Verifies CT slice thickness is <= 3mm for high precision. Limitation: Uses image Z-resolution as a proxy for slice thickness.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var image = plan.StructureSet.Image;
            double thickness = image.ZRes; 

            if (thickness > 3.0)
            {
                return Warning($"Slice thickness is {thickness}mm. Recommended <= 3mm for high precision.");
            }
            return Pass($"Slice thickness: {thickness}mm");
        }
    }

    public class UserOriginCheck : PlanCheckBase
    {
        public override string Title => "User Origin";
        public override string Category => "CT Image";
        public override string Description => "Verifies the User Origin coordinates. Limitation: Only flags (0,0,0) as a potential error; does not verify alignment with physical markers.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var image = plan.StructureSet.Image;
            var userOrigin = image.UserOrigin;
            if (userOrigin.x == 0 && userOrigin.y == 0 && userOrigin.z == 0)
            {
                return Warning("User Origin is at (0,0,0). Verify if this is intended.");
            }

            return Pass($"User Origin: ({userOrigin.x:F1}, {userOrigin.y:F1}, {userOrigin.z:F1})");
        }
    }

    public class CTDateAcquiredCheck : PlanCheckBase
    {
        public override string Title => "CT Date Acquired";
        public override string Category => "CT Image";
        public override string Description => "Displays the CT acquisition date. Limitation: Uses 'CreationDateTime' which may differ from the DICOM study date in some systems.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var image = plan.StructureSet.Image;
            
            if (image.CreationDateTime.HasValue)
            {
               return Pass($"CT Date: {image.CreationDateTime.Value}");
            }

            return Warning("CT Date could not be retrieved (CreationDateTime is null).");
        }
    }

    public class CouchStructureCheck : PlanCheckBase
    {
        public override string Title => "Couch Structure";
        public override string Category => "CT Image";
        public override string Description => "Identifies correct couch model (Thin/Medium/Thick) for dose calculation accuracy. Limitation: Relies solely on 'Couch' and type keywords being present in the structure ID.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var structures = plan.StructureSet.Structures;
            var couchStructures = structures.Where(s => s.Id.IndexOf("couch", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (!couchStructures.Any())
            {
                return Warning("No structure with 'Couch' in ID found.");
            }

            var details = new List<string>();
            string detectedType = "Unknown";
            
            foreach (var s in couchStructures)
            {
                string id = s.Id;
                string type = "Unknown";
                if (id.IndexOf("thin", StringComparison.OrdinalIgnoreCase) >= 0) type = "Thin";
                else if (id.IndexOf("medium", StringComparison.OrdinalIgnoreCase) >= 0) type = "Medium";
                else if (id.IndexOf("thick", StringComparison.OrdinalIgnoreCase) >= 0) type = "Thick";
                
                details.Add($"{id} ({type})");

                if (type != "Unknown") detectedType = type;
            }

            if (detectedType == "Unknown")
            {
                return Warning($"Couch structure found but type (Thin/Medium/Thick) not detected in ID: {string.Join(", ", details)}");
            }

            return Pass($"Couch detected: {detectedType}", string.Join("\n", details));
        }
    }
}
