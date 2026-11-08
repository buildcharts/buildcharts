const response = await fetch("/assets/environment-settings.json");
if (!response.ok) throw new Error(`Could not load settings: ${response.status}`);
const settings = await response.json();
document.querySelector("#greeting").textContent = `Hello from npm-docker in ${settings.environment}!`;
