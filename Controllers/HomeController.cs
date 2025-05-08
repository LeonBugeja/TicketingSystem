using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Diagnostics;
using TicketingSystem.Models;
using TicketingSystem.Services;

namespace TicketingSystem.Controllers;
public class HomeController : Controller
{
    private readonly PubSubService _pubSubService;

    public HomeController()
    {
        _pubSubService = new PubSubService();
    }

    public async Task<IActionResult> Index()
    {
        if (!User.Identity.IsAuthenticated)
        {
            return RedirectToAction("Index", "Login", new { area = "" });
        }

        var messages = await _pubSubService.FetchMessagesAsync(durationInSeconds: 5, acknowledge: true);
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
