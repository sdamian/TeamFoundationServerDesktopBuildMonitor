using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

namespace BuildMonitor.Services
{
    public interface IResourceManager
    {
        List<Image> GetIcons();
    }
    public class ResourceManager : IResourceManager
    {
        public List<Image> GetIcons()
        {
            var greenBallStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BuildMonitor.Images.GreenBall.gif");
            var redBallStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BuildMonitor.Images.RedBall.gif");
            var yellowBallStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BuildMonitor.Images.YellowBall.ico");

            if (greenBallStream == null || redBallStream == null || yellowBallStream == null)
            {
                throw new NullReferenceException("One of the Resource Icons is null.");
            }

            return new List<Image>() { Image.FromStream(greenBallStream), Image.FromStream(yellowBallStream), Image.FromStream(redBallStream) };
        }
    }
}