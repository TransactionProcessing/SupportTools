using System.Text.Json;

namespace StreamManagement
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            // Load up the configuration
            var streamConfiguration = await LoadStreamConfig(CancellationToken.None);

            // Add the streams to a Drop Down
            foreach (StreamConfiguration configuration in streamConfiguration) {
                this.StreamSelectionList.Items.Add(configuration.StreamName);
            }
        }

        private static async Task<List<StreamConfiguration>> LoadStreamConfig(CancellationToken cancellationToken)
        {
            // Read the identity server config json string
            String streamConfigurationJsonData = null;
            using (StreamReader sr = new StreamReader("streamconfiguration.json"))
            {
                streamConfigurationJsonData = await sr.ReadToEndAsync(cancellationToken);
            }

            StreamConfigurationList streamConfiguration = JsonSerializer.Deserialize<StreamConfigurationList>(streamConfigurationJsonData);

            return streamConfiguration.StreamConfiguration;
        }
    }

    public record StreamConfigurationList(List<StreamConfiguration> StreamConfiguration);

    public record StreamConfiguration(String StreamName, Int32 MaxAgeInDays);
}
