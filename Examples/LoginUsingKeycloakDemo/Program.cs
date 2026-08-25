using static Logger<Program>;

using var server = Server.Start();
using var client = Client.Start();

WriteLine("Press Enter to quit.");
ReadLine();
