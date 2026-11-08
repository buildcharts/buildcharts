export async function getWelcomePage(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Unexpected HTTP status: ${response.status}`);
  return response.text();
}
