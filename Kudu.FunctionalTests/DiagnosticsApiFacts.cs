﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Kudu.Client;
using Kudu.Client.Infrastructure;
using Kudu.FunctionalTests.Infrastructure;
using Kudu.TestHarness;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Kudu.FunctionalTests
{
    public class DiagnosticsApiFacts
    {
        [Fact]
        public void ConstructorTest()
        {
            string repositoryName = "Mvc3Application";
            string appName = "ConstructorTest";

            using (var repo = Git.CreateLocalRepository(repositoryName))
            {
                ApplicationManager.Run(appName, appManager =>
                {
                    using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
                    {
                        HttpResponseMessage response = client.GetAsync("diagnostics/settings").Result.EnsureSuccessful();
                        using (var reader = new JsonTextReader(new StreamReader(response.Content.ReadAsStreamAsync().Result)))
                        {
                            JObject json = (JObject)JToken.ReadFrom(reader);
                            Assert.Equal(0, json.Count);
                        }
                    }

                    using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
                    {
                        var ex = Assert.Throws<HttpUnsuccessfulRequestException>(() => client.GetAsync("diagnostics/settings/trace_level").Result.EnsureSuccessful());
                        Assert.Equal(HttpStatusCode.NotFound, ex.ResponseMessage.StatusCode);
                    }
                });
            }
        }

        [Fact]
        public async Task ProcessApiTests()
        {
            string appName = "ProcessApiTests";

            await ApplicationManager.RunAsync(appName, async appManager =>
            {
                // Test current process
                var process = await appManager.ProcessManager.GetCurrentProcessAsync();
                int currentId = process.Id;
                DateTime startTime = process.StartTime;
                Assert.NotNull(process);
                Assert.Contains("w3wp", process.Name);
                Assert.Contains("/diagnostics/processes/" + currentId, process.Href.AbsoluteUri);

                // Test get process by id
                process = await appManager.ProcessManager.GetProcessAsync(currentId);
                Assert.NotNull(process);
                Assert.Contains("w3wp", process.Name);
                Assert.Contains("/diagnostics/processes/" + currentId, process.Href.AbsoluteUri);

                // Test process list
                var processes = await appManager.ProcessManager.GetProcessesAsync();
                Assert.True(processes.Count() >= 1);
                Assert.True(processes.Any(p => p.Id == currentId));

                // Test minidump
                //var stream = new MemoryStream();
                //using (var minidump = await appManager.ProcessManager.MiniDump())
                //{
                //    Assert.NotNull(minidump);
                //    await minidump.CopyToAsync(stream);
                //}
                //Assert.True(stream.Length > 0);

                // Test kill process
                await KuduAssert.ThrowsUnwrappedAsync<HttpRequestException>(() => appManager.ProcessManager.KillProcessAsync(currentId));
                HttpUtils.WaitForSite(appManager.SiteUrl, delayBeforeRetry: 10000);
                process = await appManager.ProcessManager.GetCurrentProcessAsync();
                Assert.NotEqual(startTime, process.StartTime);
            });
        }

        [Fact]
        public void SetGetDeleteValue()
        {
            var values = new[]
            {
                new KeyValuePair<string, string>(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
                new KeyValuePair<string, string>(Guid.NewGuid().ToString(), String.Empty),
                new KeyValuePair<string, string>(Guid.NewGuid().ToString(), null)
            };

            string repositoryName = "Mvc3Application";
            string appName = "SetGetDeleteValue";

            using (var repo = Git.CreateLocalRepository(repositoryName))
            {
                ApplicationManager.Run(appName, appManager =>
                {
                    // set values
                    using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
                    {
                        JObject json = new JObject();
                        foreach (KeyValuePair<string, string> value in values)
                        {
                            json[value.Key] = value.Value;
                        }
                        client.PostAsJsonAsync("diagnostics/settings", json).Result.EnsureSuccessful();
                    }

                    // verify values
                    VerifyValues(appManager, values);

                    // delete value
                    using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
                    {
                        KeyValuePair<string, string> deleted = values[1];
                        client.DeleteAsync("diagnostics/settings/" + deleted.Key).Result.EnsureSuccessful();
                        values = values.Where(p => p.Key != deleted.Key).ToArray();
                    }

                    // update value
                    using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
                    {
                        KeyValuePair<string, string> updated = values[0] = new KeyValuePair<string, string>(values[0].Key, Guid.NewGuid().ToString());
                        JObject json = new JObject();
                        json[updated.Key] = updated.Value;
                        client.PostAsJsonAsync("diagnostics/settings", json).Result.EnsureSuccessful();
                    }

                    // verify values
                    VerifyValues(appManager, values);
                });
            }
        }

        private void VerifyValues(ApplicationManager appManager, params KeyValuePair<string, string>[] values)
        {
            using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
            {
                HttpResponseMessage response = client.GetAsync("diagnostics/settings").Result.EnsureSuccessful();
                using (var reader = new JsonTextReader(new StreamReader(response.Content.ReadAsStreamAsync().Result)))
                {
                    JObject json = (JObject)JToken.ReadFrom(reader);
                    Assert.Equal(values.Length, json.Count);
                    foreach (KeyValuePair<string, string> value in values)
                    {
                        Assert.Equal(value.Value, json[value.Key].Value<string>());
                    }
                }
            }

            foreach (KeyValuePair<string, string> value in values)
            {
                using (HttpClient client = HttpClientHelper.CreateClient(appManager.ServiceUrl, appManager.DeploymentManager.Credentials))
                {
                    if (value.Value != null)
                    {
                        HttpResponseMessage response = client.GetAsync("diagnostics/settings/" + value.Key).Result.EnsureSuccessful();
                        var result = response.Content.ReadAsStringAsync().Result;
                        Assert.Equal(value.Value, result.Trim('\"'));
                    }
                    else
                    {
                        var ex = Assert.Throws<HttpUnsuccessfulRequestException>(() => client.GetAsync("diagnostics/settings/" + value.Key).Result.EnsureSuccessful());
                        Assert.Equal(HttpStatusCode.NotFound, ex.ResponseMessage.StatusCode);
                    }
                }
            }
        }
    }
}