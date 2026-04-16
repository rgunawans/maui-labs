using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Sample.Pages;

public partial class ImagePage : ContentPage
{
	public ImagePage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		var imagePath = Path.Combine(AppContext.BaseDirectory, "dotnet_bot.png");
		if (!File.Exists(imagePath))
		{
			StreamSourceImage.IsVisible = false;
			return;
		}

		StreamSourceImage.Source = ImageSource.FromStream(() => File.OpenRead(imagePath));
		StreamSourceImage.IsVisible = true;
	}

	void AnimationStartStop_Clicked(object? sender, EventArgs e)
	{
		AnimationSwitch.IsToggled = !AnimationSwitch.IsToggled;
	}

	void UseOnlineSource_Clicked(object? sender, EventArgs e)
	{
		AnimatedGifImage.Source = ImageSource.FromUri(new Uri("https://raw.githubusercontent.com/dotnet/maui/126f47aaf9d5c01224f54fe1c6bfb1c8299cc2fe/src/Compatibility/ControlGallery/src/iOS/GifTwo.gif"));
		AnimationSwitch.IsToggled = true;
	}
}
