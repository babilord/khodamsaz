"""Exercise the unified screens against a disposable PostgreSQL app using fake SMS."""
import base64
import hashlib
import hmac
import os
from pathlib import Path
import subprocess
import sys

from playwright.sync_api import expect, sync_playwright

root = sys.argv[1].rstrip("/")
assert root.startswith(("http://127.0.0.1:", "http://localhost:"))
screenshots = Path(os.environ.get("AUTH_SCREENSHOT_DIR", "test-results/auth"))
screenshots.mkdir(parents=True, exist_ok=True)
widths = (320, 375, 390, 430, 768, 1024, 1440)
phone = "+989123456711"


def test_code():
    # Only the disposable fixture's fake challenge is inspected. No test inbox is exposed by the app.
    key = base64.b64decode(os.environ["Sms__HashKey"])
    phone_key = hmac.digest(key, f"phone:{phone}".encode(), "sha256").hex().upper()
    query = (
        """SELECT replace("Id"::text, '-', ''), "CodeHash" FROM "MobileOtpChallenges" """
        f"""WHERE "PhoneKey" = '{phone_key}' AND "Delivered" ORDER BY "IssuedAt" DESC LIMIT 1"""
    )
    challenge, expected = subprocess.check_output(
        [os.environ.get("AUTH_PSQL", "psql"), os.environ["AUTH_FIXTURE_PG"], "-XAt", "-c", query],
        text=True,
    ).strip().split("|")
    prefix = f"code:{challenge}:4:{phone_key}:unified-access:".encode()
    for candidate in range(1_000_000):
        code = f"{candidate:06d}"
        if hmac.new(key, prefix + code.encode(), hashlib.sha256).hexdigest().upper() == expected:
            return code
    raise AssertionError("No matching fake challenge code")


def capture(page, screen):
    expect(page.locator("html")).to_have_attribute("dir", "rtl")
    for width in widths:
        page.set_viewport_size({"width": width, "height": 900})
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"), (screen, width)
        assert page.locator(".access-card").bounding_box()["width"] <= 432
        expect(page.locator(".access-card button").first).to_be_visible()
        page.screenshot(path=str(screenshots / f"{screen}-{width}.png"), full_page=True)


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(channel=os.getenv("BROWSER_CHANNEL", "msedge"))
    page = browser.new_page()
    guest = browser.new_page()
    assert guest.goto(root + "/Account/Profile", wait_until="domcontentloaded").ok
    assert "/account/access?" in guest.url.lower()
    assert page.goto(root + "/account/access?returnUrl=%2FAccount%2FProfile").ok
    expect(page.locator("h1")).to_have_text("ورود یا ثبت‌نام")
    capture(page, "01-mobile")
    page.locator("#access-phone").fill("۰۹۱۲۳۴۵۶۷۱۱")
    page.locator('button[data-busy="در حال ارسال…"]').click()
    otp = page.locator("#access-code")
    expect(otp).to_be_focused()
    expect(otp).to_have_attribute("autocomplete", "one-time-code")
    expect(page.locator("[data-resend]")).to_be_disabled()
    capture(page, "02-code")
    edit = page.get_by_role("link", name="ویرایش شماره موبایل")
    assert "returnUrl=" in edit.get_attribute("href")
    page.context.grant_permissions(["clipboard-read", "clipboard-write"])
    fake_otp = test_code()
    arabic = fake_otp.translate(str.maketrans("0123456789", "٠١٢٣٤٥٦٧٨٩"))
    page.evaluate("(text) => navigator.clipboard.writeText(text)", arabic)
    otp.press("Control+v")
    expect(otp).to_have_value(fake_otp)
    expect(page.locator(".access-cells span").first).to_have_text(fake_otp[0])
    otp.press("Backspace")
    expect(page.get_by_role("button", name="تأیید و ادامه")).to_be_disabled()
    otp.press("Control+v")
    page.get_by_role("button", name="تأیید و ادامه").click()
    expect(page.locator("h1")).to_have_text("به خودم‌ساز خوش آمدید!")
    expect(page.locator("#verified-mobile")).to_have_value(phone)
    expect(page.locator("#Confirmation")).to_be_hidden()
    capture(page, "03-profile")
    page.locator("#Name").fill("کاربر آزمایشی")
    page.get_by_role("button", name="تکمیل عضویت و ورود").click()
    assert page.url.lower().endswith("/account/profile"), page.url
    assert guest.goto(root + "/Account/Login").ok
    capture(guest, "04-email")
    browser.close()

print("Four RTL screens at seven widths, OTP paste and backspace, passwordless signup and local continuation passed.")
