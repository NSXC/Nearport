using System;
using System.Collections.Generic;
using System.Device.Location;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Newtonsoft.Json;
using Spectre.Console;
using Newtonsoft.Json.Linq;
namespace ClosestAirport
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var watcher = new GeoCoordinateWatcher(GeoPositionAccuracy.Default);
            watcher.Start();

            int tryCount = 0;
            while (watcher.Status != GeoPositionStatus.Ready && tryCount < 10)
            {
                Console.WriteLine("Waiting for location...");
                await Task.Delay(1000);
                tryCount++;
            }

            GeoCoordinate userLocation;
            if (watcher.Status == GeoPositionStatus.Ready)
            {
                userLocation = watcher.Position.Location;
                Console.WriteLine($"Your location: {userLocation.Latitude}, {userLocation.Longitude}");
            }
            else
            {
                Console.WriteLine("Unable to determine location automatically. Please enter your coordinates manually.");
                Console.Write("Latitude: ");
                double latitude = double.Parse(Console.ReadLine());
                Console.Write("Longitude: ");
                double longitude = double.Parse(Console.ReadLine());
                userLocation = new GeoCoordinate(latitude, longitude);
            }

            var airports = await LoadAirportsAsync();

            var closestAirport = airports.OrderBy(a => a.Location.GetDistanceTo(userLocation)).FirstOrDefault();

            Console.WriteLine($"Closest airport: {closestAirport.Name}");
            Console.WriteLine($"City: {closestAirport.City}");
            Console.WriteLine($"Country: {closestAirport.Country}");
            string url = $"https://opensky-network.org/api/states/all?lamin={userLocation.Latitude - 1}&lomin={userLocation.Longitude - 1}&lamax={userLocation.Latitude + 1}&lomax={userLocation.Longitude + 1}";
            Console.WriteLine(url);
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    JToken json = JToken.Parse(responseString);

                    double closestDistance = Double.MaxValue;
                    JToken closestFlight = null;

                    foreach (JToken flight in json["states"])
                    {
                        double flightLat = Convert.ToDouble(flight[6]);
                        double flightLon = Convert.ToDouble(flight[5]);

                        double distance = Math.Sqrt(Math.Pow(flightLat - userLocation.Latitude, 2) + Math.Pow(flightLon - userLocation.Longitude, 2));

                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestFlight = flight;
                        }
                    }
                    if (closestFlight != null)
                    {
                        Console.WriteLine($"The closest flight is {closestFlight[1]} at a distance of {closestDistance} km");
                    }
                    else
                    {
                        Console.WriteLine("No flights found in the area");
                    }
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                }
                var mapImageUrl = GetMapImageUrl(closestAirport.Location);
                string outputPath = "airport.png";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "curl",
                    Arguments = $"-o {outputPath} {mapImageUrl}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit();
                }
                var image = new CanvasImage("airport.png");
                image.MaxWidth(18);
                AnsiConsole.Write(image);

            }//THE CORE

            static async Task<List<Airport>> LoadAirportsAsync()
            {
                var client = new HttpClient();
                var response = await client.GetAsync("https://raw.githubusercontent.com/jpatokal/openflights/master/data/airports.dat");
                var content = await response.Content.ReadAsStringAsync();
                var airports = new List<Airport>();
                using (var reader = new StringReader(content))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var values = line.Split(',');
                        if (double.TryParse(values[6], out var latitude) && double.TryParse(values[7], out var longitude))
                        {
                            var airport = new Airport
                            {
                                Name = values[1],
                                City = values[2],
                                Country = values[3],
                                Location = new GeoCoordinate(latitude, longitude)
                            };
                            airports.Add(airport);
                        }
                    }
                }
                return airports;
            }

            static string GetMapImageUrl(GeoCoordinate location)
            {
                var zoom = 14;
                var x = GetTileX(location);
                var y = GetTileY(location);
                var url = $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png";
                return url;
            }

            static int GetTileX(GeoCoordinate location)
            {
                var zoom = 14;
                var n = Math.Pow(2, zoom);
                var longitude = location.Longitude;
                var x = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
                return x;
            }

            static int GetTileY(GeoCoordinate location)
            {
                var zoom = 14;
                var n = Math.Pow(2, zoom);
                var latitude = location.Latitude * Math.PI / 180.0;
                var y = (int)Math.Floor((1 - Math.Log(Math.Tan(latitude) + 1 / Math.Cos(latitude)) / Math.PI) / 2.0 * n);
                return y;
            }
        }

        public class Airport
        {
            public string Name { get; set; }
            public string City { get; set; }
            public string Country { get; set; }
            public GeoCoordinate Location { get; set; }
        }
    }
}
