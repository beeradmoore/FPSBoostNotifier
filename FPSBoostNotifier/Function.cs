using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using HtmlAgilityPack;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace FPSBoostNotifier
{
    public class Function
    {
        ILambdaLogger _logger;

        public async Task<Output> FunctionHandler(Input input, ILambdaContext context)
        {
            _logger = context?.Logger;
            var output = new Output();

            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync("https://majornelson.com/fpsboost/");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var pageContent = await response.Content.ReadAsStringAsync();

                    var regex = new Regex(@"<table><thead>(.*)<\/tbody><\/table>");
                    var match = regex.Match(pageContent);
                    if (match.Success)
                    {
                        var gameTable = match.Groups[0].ToString();

                        List<Game> liveGamesList = null;

                        try
                        {
                            liveGamesList = ParseGameTable(gameTable);
                        }
                        catch (Exception err)
                        {
                            output.Success = false;
                            output.Message = err.Message;
                            _logger.LogLine($"ERROR: {output.Message}");
                            return output;
                        }

                        if (liveGamesList == null)
                        {
                            output.Success = false;
                            output.Message = "Could not load games list from Major Nelsons site.";
                            _logger.LogLine($"ERROR: {output.Message}");
                            return output;
                        }

                        var jsonSerializerOptions = new JsonSerializerOptions()
                        {
                            WriteIndented = true,
                        };


                        var s3Client = new AmazonS3Client(RegionEndpoint.APSoutheast2);

                        List<Game> previousGamesList = null;

                        try
                        {
                            // This won't work running locally because you don't have access to my S3 bucket.
                            var getObjectRequest = new GetObjectRequest()
                            {
                                BucketName = "files.beeradmoore.com",
                                Key = "fpsboost/current.json",
                            };
                            var getObjectResult = await s3Client.GetObjectAsync(getObjectRequest);
                            using (var streamReader = new StreamReader(getObjectResult.ResponseStream))
                            {
                                var oldJsonData = await streamReader.ReadToEndAsync();
                                previousGamesList = JsonSerializer.Deserialize<List<Game>>(oldJsonData);
                            }
                        }
                        catch (Exception err)
                        {
                            await SendSlackMessage($"Could not load fpsboost/current.json: {err.Message}");
                        }

                        if (previousGamesList == null)
                        {
                            previousGamesList = new List<Game>();
                        }


                        var newGamesList = new List<Game>();
                        var removedGamesList = new List<Game>();
                        var changedGamesList = new List<(Game OldEntry, Game NewEntry)>();

                        // Check for new games
                        foreach (var game in liveGamesList)
                        {
                            if (previousGamesList.Any(x => x.Title == game.Title) == false)
                            {
                                newGamesList.Add(game);
                            }
                        }

                        // Check for removed games
                        foreach (var game in previousGamesList)
                        {
                            if (liveGamesList.Any(x => x.Title == game.Title) == false)
                            {
                                removedGamesList.Add(game);
                            }
                        }

                        // Check for changed games
                        foreach (var liveGame in liveGamesList)
                        {
                            var previousGame = previousGamesList.FirstOrDefault(x => x.Title == liveGame.Title);
                            if (previousGame != null && previousGame.HasGameChanged(liveGame))
                            {
                                changedGamesList.Add((previousGame, liveGame));
                            }
                        }

                        if (newGamesList.Count > 0 || removedGamesList.Count > 0 || changedGamesList.Count > 0)
                        {
                            var stringBuilder = new StringBuilder();

                            // Notify of new games
                            if (newGamesList.Count > 0)
                            {
                                stringBuilder.AppendLine("New games:");
                                foreach (var newGame in newGamesList)
                                {
                                    stringBuilder.AppendLine($"- {newGame.Title}");
                                }
                                stringBuilder.AppendLine();
                            }

                            // Notify of removed games
                            if (removedGamesList.Count > 0)
                            {
                                stringBuilder.AppendLine("Removed games:");
                                foreach (var removedGame in removedGamesList)
                                {
                                    stringBuilder.AppendLine($"- {removedGame.Title}");
                                }
                                stringBuilder.AppendLine();
                            }

                            // Notify of changed games.
                            if (changedGamesList.Count > 0)
                            {
                                stringBuilder.AppendLine("Changed games:");
                                foreach (var changedGames in changedGamesList)
                                {
                                    stringBuilder.AppendLine($"- {changedGames.NewEntry.Title}");

                                    if (changedGames.OldEntry.Url != changedGames.NewEntry.Url)
                                    {
                                        stringBuilder.AppendLine($"\t- Old url: {changedGames.OldEntry.Url}");
                                        stringBuilder.AppendLine($"\t- New url: {changedGames.NewEntry.Url}");
                                    }

                                    if (changedGames.OldEntry.SeriesXFPS != changedGames.NewEntry.SeriesXFPS)
                                    {
                                        stringBuilder.AppendLine($"\t- Old Series X FPS: {changedGames.OldEntry.SeriesXFPS}");
                                        stringBuilder.AppendLine($"\t- New Series X FPS: {changedGames.NewEntry.SeriesXFPS}");
                                    }

                                    if (changedGames.OldEntry.SeriesSFPS != changedGames.NewEntry.SeriesSFPS)
                                    {
                                        stringBuilder.AppendLine($"\t- Old Series S FPS: {changedGames.OldEntry.SeriesSFPS}");
                                        stringBuilder.AppendLine($"\t- New Series S FPS: {changedGames.NewEntry.SeriesSFPS}");
                                    }

                                    if (changedGames.OldEntry.OffByDefaultSeriesX != changedGames.NewEntry.OffByDefaultSeriesX)
                                    {
                                        stringBuilder.AppendLine($"\t- Old off by default Series X: {changedGames.OldEntry.OffByDefaultSeriesX}");
                                        stringBuilder.AppendLine($"\t- New off by default Series X: {changedGames.NewEntry.OffByDefaultSeriesX}");
                                    }
                                }
                            }


                            await SendSlackMessage(stringBuilder.ToString());

                            output.Message = stringBuilder.ToString();

                            var gamesJson = JsonSerializer.Serialize(liveGamesList, jsonSerializerOptions);

                            // This won't work running locally because you don't have access to my S3 bucket.
                            var putObjectRequest = new PutObjectRequest()
                            {
                                BucketName = "files.beeradmoore.com",
                                Key = "fpsboost/current.json",
                                ContentBody = gamesJson,
                            };
                            var putObjectResult = await s3Client.PutObjectAsync(putObjectRequest);
                        }
                        else
                        {
                            output.Message = "No changes detected.";
                        }

                        output.Success = true;
                    }
                    else
                    {
                        output.Success = false;
                        output.Message = $"Unable to find HTML table to parse.";
                        _logger.LogLine($"ERROR: {output.Message}");
                        return output;
                    }
                }
                else
                {
                    output.Success = false;
                    output.Message = $"Response StatusCode not expected: {response.StatusCode}";
                    _logger.LogLine($"ERROR: {output.Message}");
                    return output;
                }
            }

            _logger.LogLine(output.Message);
            
            return output;
        }

        List<Game> ParseGameTable(string gameTable)
        {
            var games = new List<Game>();

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(gameTable);

            if (htmlDoc.DocumentNode.ChildNodes.Count == 1)
            {
                var tableNode = htmlDoc.DocumentNode.ChildNodes[0];
                if (tableNode.ChildNodes.Count == 2)
                {
                    var thead = tableNode.ChildNodes[0];
                    if (thead.ChildNodes.Count == 1 && thead.ChildNodes[0].ChildNodes.Count == 4)
                    {
                        if (thead.ChildNodes[0].ChildNodes[0].InnerHtml != "Title" ||
                            thead.ChildNodes[0].ChildNodes[1].InnerHtml != "Xbox Series X" ||
                            thead.ChildNodes[0].ChildNodes[2].InnerHtml != "Xbox Series S" ||
                            thead.ChildNodes[0].ChildNodes[3].InnerHtml != "Off by Default on Series X")
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return null;
                    }

                    var tbody = tableNode.ChildNodes[1];
                    foreach (var gameRow in tbody.ChildNodes)
                    {
                        if (gameRow.ChildNodes.Count != 4)
                        {
                            return null;
                        }

                        var game = new Game();

                        var titleNode = gameRow.ChildNodes[0];

                        if (titleNode.ChildNodes.Count == 1) // assume anchor tag
                        {
                            var anchorTag = titleNode.ChildNodes[0];
                            game.Title = System.Web.HttpUtility.HtmlDecode(anchorTag.InnerText);
                            game.Url = anchorTag.Attributes["href"].Value;
                        }
                        else
                        {
                            game.Title = System.Web.HttpUtility.HtmlDecode(titleNode.InnerText);
                        }

                        game.SeriesXFPS = gameRow.ChildNodes[1].InnerHtml switch
                        {
                            "60hz" => FPSBoost.Sixty,
                            "120hz" => FPSBoost.OneTwenty,
                            "Not Available" => FPSBoost.NotAvailable,
                            _ => throw new Exception($"Invalid FPS detected: {gameRow.ChildNodes[1].InnerHtml}"),
                        };

                        game.SeriesSFPS = gameRow.ChildNodes[2].InnerHtml switch
                        {
                            "60hz" => FPSBoost.Sixty,
                            "120hz" => FPSBoost.OneTwenty,
                            "Not Available" => FPSBoost.NotAvailable,
                            _ => throw new Exception($"Invalid FPS detected: {gameRow.ChildNodes[2].InnerHtml}"),
                        };

                        game.OffByDefaultSeriesX = gameRow.ChildNodes[3].InnerHtml switch
                        {
                            "✔" => true,
                            "" => false,
                            _ => throw new Exception($"Invalid off by default value detected: {gameRow.ChildNodes[3].InnerHtml}"),
                        };

                        games.Add(game);
                    }

                    games.Sort();

                    return games;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }


        async Task SendSlackMessage(string message)
        {
            using (var httpClient = new HttpClient())
            {
                var payload = new
                {
                    channel = "#general",
                    text = message,
                    username = "FPSBoost Notifier",
                    icon_url = "https://files.beeradmoore.com/fpsboost/majornelson_icon.jpg",
                };

                var jsonData = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                try
                {
                    var slackWebhookUrl = ""; // Your Slack webhook URL goes here.
                    var response = await httpClient.PostAsync(slackWebhookUrl, content);
                }
                catch (Exception err)
                {
                    _logger.LogLine($"ERROR: {err.Message}");
                }
            }
        }
    }
}
