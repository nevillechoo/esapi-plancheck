using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media;
using EsapiPlanCheck.Models;

namespace EsapiPlanCheck.Checks
{
    public abstract class PlanCheckBase
    {
        public abstract string Title { get; }
        public abstract string Category { get; }
        public abstract string Description { get; }

        public abstract CheckResult Execute(PlanSetup plan);

        protected Geometry CreateGeometry(VVector[][] contours)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var contour in contours)
                {
                    if (contour.Length == 0) continue;
                    ctx.BeginFigure(new System.Windows.Point(contour[0].x, contour[0].y), true, true);
                    var points = contour.Skip(1).Select(p => new System.Windows.Point(p.x, p.y)).ToList();
                    ctx.PolyLineTo(points, true, true);
                }
            }
            geometry.FillRule = FillRule.Nonzero;
            geometry.Freeze();
            return geometry;
        }

        protected CheckResult CreateResult(CheckStatus status, string message, string details = "")
        {
            return new CheckResult(Title, Category, status, message, Description, details);
        }

        protected CheckResult Pass(string message = "Check passed", string details = "")
        {
            return CreateResult(CheckStatus.Pass, message, details);
        }

        protected CheckResult Fail(string message, string details = "")
        {
            return CreateResult(CheckStatus.Fail, message, details);
        }

        protected CheckResult Warning(string message, string details = "")
        {
            return CreateResult(CheckStatus.Warning, message, details);
        }
        
        protected CheckResult NA(string message = "Not applicable", string details = "")
        {
            return CreateResult(CheckStatus.NotApplicable, message, details);
        }

        protected string GetSlicePositionString(int slice, Image image)
        {
            double zPosMm = image.Origin.z + (slice * image.ZRes);
            double zPosCm = zPosMm / 10.0;
            return $"z = {zPosCm:F2} cm";
        }
    }
}
