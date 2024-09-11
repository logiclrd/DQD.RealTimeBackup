var abortTwinkle = false;

function PickRandom(array)
{
	return array[Math.floor(Math.random() * array.length)];
}

function TwinkleFrame(lights, colours, frameDelay)
{
	if (abortTwinkle)
		return;

	for (var light of lights)
		light.style.fill = PickRandom(colours);

	setTimeout(() => TwinkleFrame(lights, colours, frameDelay), frameDelay);
}

function StartTwinkle(frameDelay)
{
	var logoContainer = document.getElementById("imgLogo");

	if (!logoContainer)
		return;

	var logo = logoContainer.contentDocument;

	if (!logo)
		return;

	if (!frameDelay)
		frameDelay = 300;

	var lights =
		[
			logo.getElementById("Light1"),
			logo.getElementById("Light2"),
			logo.getElementById("Light3"),
			logo.getElementById("Light4"),
			logo.getElementById("Light5"),
			logo.getElementById("Light6")
		];
	
	var colourSet = {};

	for (var light of lights)
		colourSet[light.style.fill] = true;

	var colours = Object.keys(colourSet);

	abortTwinkle = false;

	TwinkleFrame(lights, colours, frameDelay);
}

function StopTwinkle()
{
	abortTwinkle = true;
}