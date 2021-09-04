using System;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.TestUtilities;

namespace FPSBoostNotifier.Runner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var jsonSerializerOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
            };

            var input = new Input();

            Console.WriteLine("\n\nInput:");
            Console.WriteLine(JsonSerializer.Serialize(input, jsonSerializerOptions));

            // Invoke the lambda function and confirm the string was upper cased.
            var function = new Function();
            var context = new TestLambdaContext();
            var output = await function.FunctionHandler(input, context);

            Console.WriteLine("\n\nOutput:");
            Console.WriteLine(JsonSerializer.Serialize(output, jsonSerializerOptions));

        }
    }
}
