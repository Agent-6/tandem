using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Authentication.API.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        return View();
    }
}
