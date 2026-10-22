using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public class JawSettingCheck : PlanCheckBase
    {
        public override string Title => "Minimum Jaw Settings";
        public override string Category => "Fields";
        public override string Description => "Verifies jaw positions are >= 3x3cm to avoid narrow field issues. Limitation: Checks only the first control point of each beam.";

        public override CheckResult Execute(PlanSetup plan)
        {
            foreach (var beam in plan.Beams)
            {
                if (beam.IsSetupField) continue;
                var cp = beam.ControlPoints.FirstOrDefault();
                if (cp != null)
                {
                    double x1 = Math.Abs(cp.JawPositions.X1);
                    double x2 = Math.Abs(cp.JawPositions.X2);
                    double y1 = Math.Abs(cp.JawPositions.Y1);
                    double y2 = Math.Abs(cp.JawPositions.Y2);

                    double width = x1 + x2;
                    double height = y1 + y2;

                    if (width < 30.0 || height < 30.0)
                    {
                        return Warning($"Beam {beam.Id} has jaws < 3x3cm ({width/10:F1}x{height/10:F1}cm).");
                    }
                }
            }
            return Pass("All beams have jaws >= 3x3cm.");
        }
    }

    public class VMATCollimatorCheck : PlanCheckBase
    {
        public override string Title => "VMAT Collimator Angle";
        public override string Category => "Fields";
        public override string Description => "Ensures VMAT/Arc beams have a non-zero collimator angle to reduce inter-leaf leakage. Limitation: Only checks the first control point's collimator angle.";

        public override CheckResult Execute(PlanSetup plan)
        {
            foreach (var beam in plan.Beams)
            {
                if (beam.IsSetupField) continue;
                if (beam.Technique.Id.Contains("Arc"))
                {
                    double collAngle = beam.ControlPoints.First().CollimatorAngle;
                    if (Math.Abs(collAngle) < 0.1 || Math.Abs(collAngle - 360) < 0.1)
                    {
                        return Fail($"Beam {beam.Id} is VMAT but Collimator Angle is 0.");
                    }
                }
            }
            return Pass("VMAT Collimator angles are non-zero.");
        }
    }

    public class MUCheck : PlanCheckBase
    {
        public override string Title => "MU Values";
        public override string Category => "Fields";
        public override string Description => "Verifies MU values are within 5 to 3000 MU per beam. Limitation: Does not account for fractionation or total plan MU.";

        public override CheckResult Execute(PlanSetup plan)
        {
            foreach (var beam in plan.Beams)
            {
                if (beam.IsSetupField) continue;

                double mu = beam.Meterset.Value;
                if (mu < 5.0)
                {
                    return Warning($"Beam {beam.Id} has very low MU ({mu:F1}).");
                }
                if (mu > 3000.0)
                {
                    return Warning($"Beam {beam.Id} has very high MU ({mu:F1}).");
                }
            }
            return Pass("MU values are within nominal range (5-3000).");
        }
    }
    public class VMATApertureCheck : PlanCheckBase
    {
        public override string Title => "VMAT Aperture Check";
        public override string Category => "Fields";
        public override string Description => "Analyzes PTV-to-MLC conformity (Goal: gap < 1cm). Limitation: Vertex-projection method; uses average leaf Y-boundaries and simplified BEV geometry.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var ptv = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Contains("PTV") && !s.Id.Contains("Opt") && !s.IsEmpty);
            if (ptv == null) return NA("No PTV found for aperture check.");

            string targetId = ptv.Id;

            var mesh = ptv.MeshGeometry;
            if (mesh == null) return NA("PTV has no mesh geometry.");

            var iso = plan.Beams.FirstOrDefault()?.IsocenterPosition;
            if (!iso.HasValue) return Fail("Could not determine isocenter.");

            var excessGapHistogram = new SortedDictionary<double, int>();
            for (int i = 0; i <= 30; i++)
            {
                excessGapHistogram.Add(Math.Round(i * 0.1, 1), 0);
            }
            
            var debugLogs = new List<string>();
            var warnings = new List<string>();
            int checkedBeams = 0;
            long totalOpenLeavesChecked = 0;

            foreach (var beam in plan.Beams)
            {
                if (beam.IsSetupField) continue;
                bool isVmat = beam.MLCPlanType == MLCPlanType.VMAT || beam.Technique.Id.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isVmat) continue;

                checkedBeams++;
                
                int numPairs = beam.ControlPoints[0].LeafPositions.GetLength(1);
                var leafYBounds = GetLeafYBoundaries(numPairs, beam.MLC.Model);

                int step = 1;
                for (int i = 0; i < beam.ControlPoints.Count; i += step)
                {
                    var cp = beam.ControlPoints[i];
                    var projectedTriangles = ProjectTrianglesToBEV(mesh, iso.Value, cp.GantryAngle, cp.CollimatorAngle, cp.PatientSupportAngle);
                    var ptvRanges = ComputePtvRanges(projectedTriangles, leafYBounds);
                    if (i == 0)
                    {
                        
                    }
                    float[,] leafPos = cp.LeafPositions;

                    for (int leafIdx = 0; leafIdx < numPairs; leafIdx++)
                    {
                        double leafWidth = - leafPos[0, leafIdx] + leafPos[1, leafIdx];
                        if (leafWidth < 0.01) continue;
                        Func<int, double[]> GetPtvRange = (idx) => {
                            if (idx < 0 || idx >= numPairs) return null;
                            if (ptvRanges.ContainsKey(idx))
                            {
                                var r = ptvRanges[idx];
                                return new double[] { r.Item1, r.Item2 };
                            }
                            return null;
                        };
                        var currentRange = GetPtvRange(leafIdx);
                        double excessGapCm = 0;
                        double currentExcess = 0;
                        if (currentRange == null)
                        {
                            currentExcess = leafWidth / 10.0;
                        }
                        else
                        {
                            currentExcess = (leafWidth - (currentRange[1] - currentRange[0])) / 10.0;
                        }
                        double usedPtvWidth = 0;
                        string usedSource = "none";
                        if (currentRange != null)
                        {
                            usedPtvWidth = currentRange[1] - currentRange[0];
                            usedSource = "curr";
                        }
                        
                        double bestExcess = currentExcess;
                        if (currentExcess > 1.0 || (currentRange == null && leafWidth > 0.01))
                        {
                            bool foundPtvIn1 = false;
                            var range1Plus = GetPtvRange(leafIdx + 1);
                            var range1Minus = GetPtvRange(leafIdx - 1);

                            if (range1Plus != null)
                            {
                                double ptvW = range1Plus[1] - range1Plus[0];
                                double excess1Plus = (leafWidth - ptvW) / 10.0;
                                if (usedSource == "none" || excess1Plus < bestExcess) 
                                {
                                    bestExcess = excess1Plus;
                                    usedPtvWidth = ptvW;
                                    usedSource = "+1";
                                }
                                foundPtvIn1 = true;
                            }
                            if (range1Minus != null)
                            {
                                double ptvW = range1Minus[1] - range1Minus[0];
                                double excess1Minus = (leafWidth - ptvW) / 10.0;
                                if (usedSource == "none" || excess1Minus < bestExcess) 
                                {
                                    bestExcess = excess1Minus;
                                    usedPtvWidth = ptvW;
                                    usedSource = "-1";
                                }
                                foundPtvIn1 = true;
                            }
                            if (usedSource == "none" || bestExcess > 1.0) 
                            {
                                var range2Plus = GetPtvRange(leafIdx + 2);
                                var range2Minus = GetPtvRange(leafIdx - 2);

                                if (range2Plus != null)
                                {
                                    double ptvW = range2Plus[1] - range2Plus[0];
                                    double excess2Plus = (leafWidth - ptvW) / 10.0;
                                    if (excess2Plus < bestExcess) 
                                    {
                                        bestExcess = excess2Plus;
                                        usedPtvWidth = ptvW;
                                        usedSource = "+2";
                                    }
                                }
                                if (range2Minus != null)
                                {
                                    double ptvW = range2Minus[1] - range2Minus[0];
                                    double excess2Minus = (leafWidth - ptvW) / 10.0;
                                    if (usedSource == "none" || excess2Minus < bestExcess) 
                                    {
                                        bestExcess = excess2Minus;
                                        usedPtvWidth = ptvW;
                                        usedSource = "-2";
                                    }
                                }
                            }
                        }
                        excessGapCm = bestExcess;
                        double failureThreshold = (usedSource == "none") ? 0.2 : 1.1;
                        if (excessGapCm < 0) excessGapCm = 0; 
                        
                        totalOpenLeavesChecked++;

                        double bin = Math.Floor(excessGapCm * 10) / 10.0;
                        if (bin > 3.0) bin = 3.0; 

                        if (excessGapHistogram.ContainsKey(bin))
                        {
                            excessGapHistogram[bin]++;
                        }
                        else
                        {
                            if (bin >= 3.0) excessGapHistogram[3.0]++;
                        }
                    }
                }
            }
            
            if (checkedBeams == 0) return NA("No VMAT beams found.");
            var details = new List<string>();
            details.Add($"Target: {targetId}");
            long countOver1cm = 0;
            foreach(var kvp in excessGapHistogram)
            {
                if (kvp.Key >= 1.1 && kvp.Value > 0) countOver1cm += kvp.Value;
            }
            details.Add($"Fails: {countOver1cm}");
            details.Add("Histogram:");
            foreach (var kvp in excessGapHistogram)
            {
                if (kvp.Value == 0) continue;
                string label = (kvp.Key >= 3.0) ? ">3.0 cm" : $"{kvp.Key:F1}-{kvp.Key + 0.1:F1} cm";
                details.Add($"{label}: {kvp.Value}");
            }
            

            if (countOver1cm > 0 || warnings.Count > 0)
            {
                details.Add("");
                details.Add($"WARNING: Found {countOver1cm} leaf pairs with excess gap > 1.1 cm.");
                if (warnings.Count > 0) details.AddRange(warnings);
                
                return Warning($"Excess gaps > 1cm detected ({countOver1cm} instances).", string.Join("\n", details));
            }

            return Pass($"Checked {checkedBeams} VMAT beams. Appears conformal (Max gap <= 1.1cm).", string.Join("\n", details));
        }
        private struct BevTriangle
        {
            public System.Windows.Point P1;
            public System.Windows.Point P2;
            public System.Windows.Point P3;
            public double MinY;
            public double MaxY;
        }

        private List<BevTriangle> ProjectTrianglesToBEV(MeshGeometry3D mesh, VVector iso, double gantryAngle, double collimatorAngle, double couch)
        {
            var triangles = new List<BevTriangle>();
            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;

            if (indices == null || positions == null) return triangles;

            double gimr = gantryAngle * Math.PI / 180.0;
            double cimr = collimatorAngle * Math.PI / 180.0;
            double cosG = Math.Cos(gimr);
            double sinG = Math.Sin(gimr);
            double cosC = Math.Cos(cimr);
            double sinC = Math.Sin(cimr);
            var projectedVerts = new System.Windows.Point[positions.Count];
            for(int i=0; i<positions.Count; i++)
            {
                var pt = positions[i];
                double dx = pt.X - iso.x;
                double dy = pt.Y - iso.y;
                double dz = pt.Z - iso.z;

                double rotX = dx * cosG - dy * sinG;
                double finalX = rotX * cosC - dz * sinC;
                double finalY = -(rotX * sinC + dz * cosC);
                projectedVerts[i] = new System.Windows.Point(finalX, finalY);
            }
            for (int t = 0; t < indices.Count; t += 3)
            {
                var p1 = projectedVerts[indices[t]];
                var p2 = projectedVerts[indices[t+1]];
                var p3 = projectedVerts[indices[t+2]];

                triangles.Add(new BevTriangle 
                { 
                    P1 = p1, P2 = p2, P3 = p3,
                    MinY = Math.Min(p1.Y, Math.Min(p2.Y, p3.Y)),
                    MaxY = Math.Max(p1.Y, Math.Max(p2.Y, p3.Y))
                });
            }
            return triangles;
        }
        private Dictionary<int, Tuple<double, double>> ComputePtvRanges(List<BevTriangle> triangles, List<Tuple<double, double>> leafBounds)
        {
            var ranges = new Dictionary<int, double[]>(); 

            int numLeaves = leafBounds.Count;
            
            foreach(var tri in triangles)
            {
                
                for (int i = 0; i < numLeaves; i++)
                {
                    double leafMinY = leafBounds[i].Item1;
                    double leafMaxY = leafBounds[i].Item2;
                    if (leafMaxY <= tri.MinY || leafMinY >= tri.MaxY) continue;
                    
                    double minX = double.MaxValue;
                    double maxX = double.MinValue;
                    bool hit = false;
                    if (tri.P1.Y >= leafMinY && tri.P1.Y < leafMaxY) { minX = Math.Min(minX, tri.P1.X); maxX = Math.Max(maxX, tri.P1.X); hit = true; }
                    if (tri.P2.Y >= leafMinY && tri.P2.Y < leafMaxY) { minX = Math.Min(minX, tri.P2.X); maxX = Math.Max(maxX, tri.P2.X); hit = true; }
                    if (tri.P3.Y >= leafMinY && tri.P3.Y < leafMaxY) { minX = Math.Min(minX, tri.P3.X); maxX = Math.Max(maxX, tri.P3.X); hit = true; }
                    CheckEdgeIntersect(tri.P1, tri.P2, leafMinY, ref minX, ref maxX, ref hit);
                    CheckEdgeIntersect(tri.P1, tri.P2, leafMaxY, ref minX, ref maxX, ref hit);
                    CheckEdgeIntersect(tri.P2, tri.P3, leafMinY, ref minX, ref maxX, ref hit);
                    CheckEdgeIntersect(tri.P2, tri.P3, leafMaxY, ref minX, ref maxX, ref hit);
                    CheckEdgeIntersect(tri.P3, tri.P1, leafMinY, ref minX, ref maxX, ref hit);
                    CheckEdgeIntersect(tri.P3, tri.P1, leafMaxY, ref minX, ref maxX, ref hit);

                    if (hit)
                    {
                        if (!ranges.ContainsKey(i)) ranges[i] = new double[] { minX, maxX };
                        else
                        {
                            ranges[i][0] = Math.Min(ranges[i][0], minX);
                            ranges[i][1] = Math.Max(ranges[i][1], maxX);
                        }
                    }
                }
            }

            var result = new Dictionary<int, Tuple<double, double>>();
            foreach(var kvp in ranges)
            {
                result[kvp.Key] = Tuple.Create(kvp.Value[0], kvp.Value[1]);
            }
            return result;
        }

        private void CheckEdgeIntersect(System.Windows.Point p1, System.Windows.Point p2, double cutY, ref double minX, ref double maxX, ref bool hit)
        {
            if ((p1.Y < cutY && p2.Y > cutY) || (p1.Y > cutY && p2.Y < cutY))
            {
                double t = (cutY - p1.Y) / (p2.Y - p1.Y);
                double ix = p1.X + t * (p2.X - p1.X);
                minX = Math.Min(minX, ix);
                maxX = Math.Max(maxX, ix);
                hit = true;
            }
        }

        private List<Tuple<double, double>> GetLeafYBoundaries(int numPairs, string mlcModel)
        {
            var bounds = new List<Tuple<double, double>>();
            bool isHD = !string.IsNullOrEmpty(mlcModel) && (
                mlcModel.IndexOf("HD", StringComparison.OrdinalIgnoreCase) >= 0 || 
                mlcModel.IndexOf("High Definition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mlcModel.Contains("120HighResolution"));
            if (numPairs == 60 && isHD)
            {
                double currentY = 110.0;
                for (int i = 0; i < 60; i++)
                {
                    double height;
                    if (i < 14 || i >= 46)
                        height = 5.0;
                    else
                        height = 2.5;

                    bounds.Add(Tuple.Create(currentY - height, currentY));
                    currentY -= height;
                }
            }
            else if (numPairs == 60)
            {
                double currentY = 200.0;
                for (int i = 0; i < 60; i++)
                {
                    double height;
                    if (i < 10 || i >= 50)
                        height = 10.0;
                    else
                        height = 5.0;

                    bounds.Add(Tuple.Create(currentY - height, currentY));
                    currentY -= height;
                }
            }
            else
            {
                double totalHeight = 400.0;
                if (numPairs > 0) totalHeight = 400.0;
                if (numPairs == 40) totalHeight = 400.0;
                
                double leafHeight = totalHeight / numPairs;
                double currentY = totalHeight / 2.0;
                
                for (int i = 0; i < numPairs; i++)
                {
                    bounds.Add(Tuple.Create(currentY - leafHeight, currentY));
                    currentY -= leafHeight;
                }
            }
            return bounds;
        }
    }

    public class CollimatorAngleDiversityCheck : PlanCheckBase
    {
        public override string Title => "Collimator Angle Diversity";
        public override string Category => "Fields";
        public override string Description => "Flags plans where all beams share a single collimator angle. Limitation: Recommendations for diversity are generic and may not apply to all clinical cases.";

        public override CheckResult Execute(PlanSetup plan)
        {
            var treatmentBeams = plan.Beams.Where(b => !b.IsSetupField).ToList();
            if (treatmentBeams.Count <= 1) return Pass("Single beam plan.");

            var uniqueAngles = treatmentBeams.Select(b => Math.Round(b.ControlPoints.First().CollimatorAngle, 1)).Distinct().ToList();

            if (uniqueAngles.Count == 1)
            {
                return Warning($"All beams have the same Collimator Angle ({uniqueAngles[0]:F1}). Consideration for diversity recommended.");
            }
            
            return Pass($"Collimator angles are varied ({string.Join(", ", uniqueAngles)})");
        }
    }

    public class StaticFieldSizeCheck : PlanCheckBase
    {
        public override string Title => "Static Field Size";
        public override string Category => "Fields";
        public override string Description => "Ensures static field dimensions are at least 2x2cm for reliable dose calculation. Limitation: Based on jaw positions at the first control point.";

        public override CheckResult Execute(PlanSetup plan)
        {
            foreach (var beam in plan.Beams)
            {
                if (beam.IsSetupField) continue;
                if (beam.Technique.Id.Contains("Arc") || beam.MLCPlanType == MLCPlanType.VMAT) continue;

                var cp = beam.ControlPoints.FirstOrDefault();
                if (cp != null)
                {
                    double x1 = Math.Abs(cp.JawPositions.X1);
                    double x2 = Math.Abs(cp.JawPositions.X2);
                    double y1 = Math.Abs(cp.JawPositions.Y1);
                    double y2 = Math.Abs(cp.JawPositions.Y2);

                    double width = x1 + x2;
                    double height = y1 + y2;

                    if (width < 20.0 || height < 20.0)
                    {
                        return Fail($"Beam {beam.Id} (Static) has small field < 2x2cm ({width / 10:F1}x{height / 10:F1}cm).");
                    }
                }
            }
            return Pass("All static fields are > 2x2cm.");
        }
    }
}
