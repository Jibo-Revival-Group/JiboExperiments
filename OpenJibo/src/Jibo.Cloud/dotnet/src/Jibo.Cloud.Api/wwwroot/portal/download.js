async function downloadPortalFile(response, fallbackName) {
  const blob = await response.blob();
  if (!blob.size) throw new Error("This download is empty.");

  const disposition = response.headers.get("Content-Disposition") || "";
  const filename = disposition.match(/(?:^|;)\s*filename="?([^";]+)/i)?.[1]?.trim() || fallbackName;
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
