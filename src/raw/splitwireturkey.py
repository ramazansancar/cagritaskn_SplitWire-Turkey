import os
import sys
import subprocess
import time
import ctypes
import tkinter as tk
from tkinter import messagebox, filedialog
import requests
import threading
import shutil
import string
import tkinter.ttk as ttk
from ttkthemes import ThemedTk
import webbrowser
import tkinter.font as tkFont
from PIL import Image, ImageTk, ImageSequence


WGCF_EXECUTABLE = "wgcf.exe"
WIRE_SOCK_DOWNLOAD_URL = "https://wiresock.net/_api/download-release.php?product=wiresock-secure-connect&platform=windows_x64&version=latest"
WIRE_SOCK_INSTALLER_NAME = "WireSockInstaller.exe"
CONF_FILE_NAME = "SW-Turkey.conf"
LOGO_FILENAME = "splitwire-logo-128.png"
TEXT_FILENAME = "splitwireturkeytext.png"

def get_resource_path(relative_path):
    # Always use the directory of the executable or script with /res subfolder
    base_path = os.path.dirname(sys.executable if getattr(sys, 'frozen', False) else __file__)
    res_path = os.path.join(base_path, "res")
    
    # Create /res folder if it doesn't exist
    if not os.path.exists(res_path):
        os.makedirs(res_path)
    
    return os.path.join(res_path, relative_path)

def show_info():
    result = messagebox.askyesno(
        "Bilgi",
        "SplitWire-Turkey © 2025 Çağrı Taşkın\n\n"
        "Daha fazla bilgi ve kaynak kodu için Github sayfasını ziyaret etmek ister misiniz?"
    )
    if result:
        webbrowser.open("https://github.com/cagritaskn/SplitWire-Turkey")

def is_admin():
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False

def find_wiresock_path():
    for drive in string.ascii_uppercase:
        base_path = f"{drive}:\\Program Files\\WireSock Secure Connect"
        if os.path.isdir(base_path):
            return os.path.join(base_path, "bin", "wiresock-client.exe")
        base_path_x86 = f"{drive}:\\Program Files (x86)\\WireSock Secure Connect"
        if os.path.isdir(base_path_x86):
            return os.path.join(base_path_x86, "bin", "wiresock-client.exe")
    return None

def terminate_process_by_name(name):
    try:
        # Use taskkill command instead of psutil
        subprocess.run(["taskkill", "/f", "/im", name], 
                      stdout=subprocess.DEVNULL, 
                      stderr=subprocess.DEVNULL,
                      creationflags=subprocess.CREATE_NO_WINDOW)
    except:
        pass

def wgcf_profile_create(callback=None, extra_folders=None):
    wgcf_path = get_resource_path(WGCF_EXECUTABLE)
    if not os.path.exists(wgcf_path):
        messagebox.showerror("Hata", "wgcf.exe bulunamadı.")
        return

    exe_dir = os.path.dirname(sys.executable if getattr(sys, 'frozen', False) else __file__)
    res_dir = os.path.join(exe_dir, "res")
    if not os.path.exists(res_dir):
        os.makedirs(res_dir)
    temp_acc_file = os.path.join(res_dir, "wgcf-account.toml")

    def run_commands():
        try:
            # Check if account file already exists and handle it
            if os.path.exists(temp_acc_file):
                # Try to remove the existing account file to allow fresh registration
                try:
                    os.remove(temp_acc_file)
                except:
                    pass  # If we can't remove it, continue anyway
            
            # ToS otomatik kabul ile register komutu, gizli pencereyle çalıştırılıyor
            proc = subprocess.Popen(
                [wgcf_path, "register", "--accept-tos"],
                cwd=res_dir,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=subprocess.CREATE_NO_WINDOW
            )
            stdout, stderr = proc.communicate()
            
            if proc.returncode != 0:
                error_msg = stderr.decode() if stderr else "Unknown error"
                raise Exception(f"Register işlemi başarısız oldu. Hata: {error_msg}")

            result2 = subprocess.run(
                [wgcf_path, "generate"],
                cwd=res_dir,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=subprocess.CREATE_NO_WINDOW
            )

            if result2.returncode != 0:
                raise Exception(f"Generate işlemi başarısız oldu:\n{result2.stderr.decode()}")

            profile_path = os.path.join(res_dir, "wgcf-profile.conf")
            if os.path.exists(profile_path):
                final_conf_path = modify_and_rename_conf(extra_folders)
                if final_conf_path:
                    if callback:
                        time.sleep(1)
                        callback(final_conf_path)
            else:
                raise FileNotFoundError(f"Profil dosyası bulunamadı.\n{profile_path}")
        except Exception as e:
            messagebox.showerror("Hata", str(e))

    threading.Thread(target=run_commands).start()

def modify_and_rename_conf(extra_folders=None):
    try:
        res_dir = os.path.join(os.path.dirname(sys.executable if getattr(sys, 'frozen', False) else __file__), "res")
        if not os.path.exists(res_dir):
            os.makedirs(res_dir)
        original_path = os.path.join(res_dir, "wgcf-profile.conf")
        if not os.path.exists(original_path):
            raise FileNotFoundError("wgcf-profile.conf dosyası bulunamadı.")

        with open(original_path, "r", encoding="utf-8") as file:
            lines = file.readlines()

        username = os.getlogin()
        discord_path = f"C:\\Users\\{username}\\AppData\\Local\\Discord"
        root = tk.Tk()
        root.withdraw()

        if not os.path.isdir(discord_path):
            messagebox.showwarning("Uyarı", "Discord klasörü bulunamadı. Lütfen Discord klasörünü seçiniz.")
            selected_dir = filedialog.askdirectory(title="Discord klasörünü seçiniz")
            if not selected_dir:
                messagebox.showerror("Hata", "Discord klasörü seçilmedi. İşlem iptal edildi.")
                root.destroy()
                return None
            discord_path = selected_dir
        root.destroy()

        # Klasör yollarını normalize et (Windows için ters eğik çizgi) ve tekrar virgülsüz bir liste oluştur
        app_paths = [
            os.path.normpath(discord_path),
            "discord",
            "roblox",
            "Discord.exe",
            "Update.exe",
            "RobloxPlayerBeta.exe",
            "RobloxPlayerInstaller.exe"
        ]

        if extra_folders:
            for folder in extra_folders:
                if folder.strip():  # boşluklardan arındır, geçerliyse ekle
                    app_paths.append(os.path.normpath(folder.strip()))

        allowed_apps_line = f"AllowedApps = {', '.join(app_paths)}\n"

        new_lines = []
        for line in lines:
            new_lines.append(line)
            if line.strip().startswith("Endpoint"):
                new_lines.append(allowed_apps_line)

        res_dir = os.path.join(os.path.dirname(sys.executable if getattr(sys, 'frozen', False) else __file__), "res")
        if not os.path.exists(res_dir):
            os.makedirs(res_dir)
        conf_path = os.path.join(res_dir, CONF_FILE_NAME)
        with open(conf_path, "w", encoding="utf-8") as file:
            file.writelines(new_lines)

        os.remove(original_path)
        return conf_path

    except Exception as e:
        messagebox.showerror("Hata", f".conf dosyası düzenlenirken hata:\n{e}")
        return None


def install_wiresock_service(conf_path):
    def run():
        try:
            wiresock_exe = find_wiresock_path()
            if not wiresock_exe or not os.path.exists(wiresock_exe):
                raise FileNotFoundError("WireSock kurulumu bulunamadı.")

            result = subprocess.run([
                wiresock_exe,
                "install",
                "-start-type", "2",
                "-config", conf_path,
                "-log-level", "none"
            ], stdout=subprocess.PIPE, stderr=subprocess.PIPE)

            if result.returncode == 0:
                subprocess.run(["sc", "start", "wiresock-client-service"], shell=True)
                messagebox.showinfo("Başarılı", "WireSock hizmeti kuruldu ve başlatıldı.")
            else:
                messagebox.showerror("Hata", f"Kurulum başarısız:\n{result.stderr.decode()}")

        except Exception as e:
            messagebox.showerror("Hata", f"Hizmet kurulumu sırasında hata:\n{e}")

    threading.Thread(target=run).start()

def download_and_run_wiresock(update_ui_callback=None):
    def task():
        try:
            response = requests.get(WIRE_SOCK_DOWNLOAD_URL, verify=False)
            with open(WIRE_SOCK_INSTALLER_NAME, "wb") as f:
                f.write(response.content)
            subprocess.run(WIRE_SOCK_INSTALLER_NAME, shell=True)
            terminate_process_by_name("WiresockConnect.exe")
            os.remove(WIRE_SOCK_INSTALLER_NAME)
            if update_ui_callback:
                update_ui_callback()
        except Exception as e:
            messagebox.showerror("Hata", f"İndirme veya çalıştırma hatası:\n{e}")

    threading.Thread(target=task).start()

def remove_wiresock_service():
    answer = messagebox.askyesno("Emin misiniz?", "WireSock hizmeti sistemden kaldırılacak. Devam etmek istiyor musunuz?")
    if not answer:
        return

    try:
        subprocess.run(["sc", "stop", "wiresock-client-service"], shell=True)
        subprocess.run(["sc", "delete", "wiresock-client-service"], shell=True)
        messagebox.showinfo("Başarılı", "WireSock hizmeti başarıyla kaldırıldı.")
    except Exception as e:
        messagebox.showerror("Hata", f"Hizmet kaldırılamadı:\n{e}")

loading_overlay = None
loading_label = None

loading_overlay = None
loading_label = None
loading_frames = []
loading_animation_running = False

def show_loading_gif(parent):
    global loading_overlay, loading_label, loading_frames, loading_animation_running

    if loading_overlay is not None:
        return

    gif_path = get_resource_path("loading.gif")
    gif = Image.open(gif_path)
    loading_frames = [ImageTk.PhotoImage(frame.convert("RGBA")) for frame in ImageSequence.Iterator(gif)]

    loading_overlay = tk.Toplevel(parent)
    loading_overlay.overrideredirect(True)
    loading_overlay.attributes("-topmost", True)
    loading_overlay.wm_attributes("-transparentcolor", "white")
    loading_overlay.configure(bg="white")

    loading_label = tk.Label(loading_overlay, bg="white")
    loading_label.pack(expand=True)

    def update_overlay_position():
        if not loading_overlay or not loading_overlay.winfo_exists():
            return
        x = parent.winfo_rootx() + parent.winfo_width() // 2 - 100
        y = parent.winfo_rooty() + parent.winfo_height() // 2 - 100
        loading_overlay.geometry(f"200x200+{x}+{y}")

    # İlk konumlandırma
    update_overlay_position()

    # Ana pencere hareket ettiğinde overlay'i güncelle
    def on_parent_configure(event=None):
        update_overlay_position()

    parent.bind("<Configure>", on_parent_configure)

    loading_overlay.lift()
    loading_overlay.focus_force()
    loading_overlay.update_idletasks()
    loading_overlay.update()
    loading_overlay.grab_set()

    loading_animation_running = True

    def animate(index=0):
        if not loading_animation_running:
            return
        frame = loading_frames[index]
        loading_label.config(image=frame)
        loading_label.image = frame
        loading_overlay.after(100, animate, (index + 1) % len(loading_frames))

    animate()





def hide_loading_gif():
    global loading_overlay, loading_label, loading_frames, loading_animation_running
    loading_animation_running = False
    if loading_overlay:
        loading_overlay.grab_release()
        loading_overlay.destroy()
        loading_overlay = None
        loading_label = None
        loading_frames = []



def main_gui():
    if not is_admin():
        root = tk.Tk()
        root.withdraw()
        messagebox.showwarning(
            "Uyarı",
            "Yönetici olarak çalıştırılmadı. Lütfen yönetici olarak çalıştırın."
        )
        root.destroy()
        sys.exit(0)
    else:
        root3 = tk.Tk()
        root3.withdraw()
        messagebox.showinfo("Bilgi", "Yönetici izinleri alındı.")
        root3.destroy()

    root = ThemedTk(theme="adapta")
    root.configure(bg="#FFFFFF")
    root.title("SplitWire-Turkey")
    root.geometry("470x470")
    root.resizable(False, False)
    icon_path = get_resource_path("splitwire.ico")
    if os.path.exists(icon_path):
        try:
            root.iconbitmap(icon_path)
        except:
            pass  # Ignore icon errors

    main_frame = tk.Frame(root, bg="#FFFFFF")
    settings_frame = tk.Frame(root, bg="#FFFFFF")
    for frame in (main_frame, settings_frame):
        frame.place(x=0, y=0, width=470, height=470)

    def show_frame(frame):
        frame.tkraise()

    def confirm_exit():
        result = messagebox.askyesno("Çıkış", "Uygulamadan çıkmak istediğinize emin misiniz?")
        if result:
            root.destroy()

    logo_path = get_resource_path(LOGO_FILENAME)
    if os.path.exists(logo_path):
        try:
            logo_image = tk.PhotoImage(file=logo_path)
            logo_label = tk.Label(main_frame, image=logo_image, bg="#FFFFFF")
            logo_label.image = logo_image
            logo_label.pack(pady=(10, 0))
        except:
            pass  # Ignore logo errors

    text_path = get_resource_path(TEXT_FILENAME)
    if os.path.exists(text_path):
        try:
            text_image = tk.PhotoImage(file=text_path)
            text_label = tk.Label(main_frame, image=text_image, bg="#FFFFFF")
            text_label.image = text_image
            text_label.pack(pady=(0, 5))
        except:
            pass  # Ignore text image errors

    def update_wiresock_ui():
        wiresock_path = find_wiresock_path()
        if wiresock_path:
            wiresock_status_label.config(text="", foreground="green")
        else:
            wiresock_status_label.config(text="WireSock yüklü değil!", foreground="red")

    def auto_update_wiresock():
        while True:
            time.sleep(5)
            root.after(0, update_wiresock_ui)

    threading.Thread(target=auto_update_wiresock, daemon=True).start()

    def run_fast_setup():
        confirm = messagebox.askyesno("Onay", "Hızlı kurulum başlatmak istediğinizden emin misiniz?")
        if not confirm:
            return
        
        
        wiresock_path = find_wiresock_path()
        if not wiresock_path:
            answer = messagebox.askyesno(
                "WireSock Yüklü Değil",
                "WireSock uygulaması yüklü değil.\nHızlı kurulum için önce WireSock'u yüklemelisiniz.\n\nWireSock yüklenip kurulsun mu ?"
            )
            if answer:
                show_loading_gif(root)
                download_and_run_wiresock(lambda: [update_wiresock_ui(), hide_loading_gif()])
            else:
                messagebox.showinfo("İptal", "Kurulum iptal edildi.")
            return

        show_loading_gif(root)
        wgcf_profile_create(lambda conf_path: [install_wiresock_service(conf_path), hide_loading_gif()])

    ttk.Button(main_frame, text="Başlat", command=run_fast_setup).pack(pady=5)
    ttk.Button(main_frame, text="Gelişmiş", command=lambda: show_frame(settings_frame)).pack(pady=5)
    ttk.Button(main_frame, text="Çıkış", command=confirm_exit).pack(pady=5)
    wiresock_status_label = tk.Label(main_frame, font=("Arial", 11), bg="#FFFFFF")
    wiresock_status_label.pack(pady=5)
    info_button = ttk.Button(main_frame, text="ℹ", width=2, command=show_info)
    info_button.pack(pady=(5, 0))

    # Klasör yönetimi listesi
    folder_list = []

    def add_folder():
        folder = filedialog.askdirectory(title="Klasör Seç")
        if folder and folder not in folder_list:
            folder_list.append(folder)
            update_folder_listbox()

    def remove_selected_folder(index):
        if 0 <= index < len(folder_list):
            del folder_list[index]
            update_folder_listbox()

    def update_folder_listbox():
        for widget in folder_list_frame.winfo_children():
            widget.destroy()

        label_font = tkFont.Font(family="TkDefaultFont", size=10)

        for i, folder in enumerate(folder_list):
            frame = tk.Frame(folder_list_frame, bg="#FFFFFF")
            frame.pack(fill='x', pady=1)

            display_text = truncate_path_middle(folder, max_width=390, font=label_font)

            label = tk.Label(frame, text=display_text, bg="#FFFFFF", anchor="w", font=label_font)
            label.pack(side="left", fill="x", expand=True)

            remove_btn = ttk.Button(frame, text=" - ", width=1, command=lambda idx=i: remove_selected_folder(idx))
            remove_btn.pack(side="right")



    def truncate_path_middle(path, max_width, font):
        if font.measure(path) <= max_width:
            # Genişlik küçükse boşluk ekle
            space_width = font.measure(" ")
            if space_width == 0:
                return path  # Güvenlik için
            pad_spaces = max(0, (max_width - font.measure(path) - 8)) // space_width
            return path + (" " * pad_spaces)

        ellipsis = "..."
        ellipsis_width = font.measure(ellipsis)

        left_len = 0
        right_len = 0

        while True:
            left_part = path[:left_len]
            right_part = path[-right_len:] if right_len > 0 else ""
            combined = left_part + ellipsis + right_part
            width = font.measure(combined)

            if width > max_width:
                break

            if left_len + right_len < len(path) - 1:
                left_len += 1
                right_len += 1
            else:
                break

        left_part = path[:left_len - 1]
        right_part = path[-(right_len - 1):] if right_len > 1 else ""
        return left_part + ellipsis + right_part




    def run_custom_setup():
        wiresock_path = find_wiresock_path()
        if not wiresock_path:
            answer = messagebox.askyesno(
                "WireSock Yüklü Değil",
                "WireSock uygulaması yüklü değil.\nHızlı kurulum için önce WireSock'u yüklemelisiniz.\n\nWireSock yüklenip kurulsun mu ?"
            )
            if answer:
                download_and_run_wiresock(update_wiresock_ui)
            else:
                messagebox.showinfo("İptal", "Kurulum iptal edildi.")
            return
            
        confirm = messagebox.askyesno("Onay", "Özelleştirilmiş hızlı kurulum başlatmak istediğinizden emin misiniz?")
        if not confirm:
            return
        
        show_loading_gif(root)
        wgcf_profile_create(lambda conf_path: [install_wiresock_service(conf_path), hide_loading_gif()], folder_list)
        
    def generate_custom_conf_only():
        confirm = messagebox.askyesno("Onay", "Özelleştirilmiş profil dosyası oluşturmak istediğinizden emin misiniz?")
        if not confirm:
            return

        wgcf_path = get_resource_path(WGCF_EXECUTABLE)
        if not os.path.exists(wgcf_path):
            messagebox.showerror("Hata", "wgcf.exe bulunamadı.")
            return

        exe_dir = os.path.dirname(sys.executable if getattr(sys, 'frozen', False) else __file__)
        res_dir = os.path.join(exe_dir, "res")
        if not os.path.exists(res_dir):
            os.makedirs(res_dir)
        temp_acc_file = os.path.join(res_dir, "wgcf-account.toml")
        temp_conf_path = os.path.join(res_dir, "wgcf-profile.conf")

        def run():
            try:
                show_loading_gif(root)
                
                # Check if account file already exists and handle it
                if os.path.exists(temp_acc_file):
                    # Try to remove the existing account file to allow fresh registration
                    try:
                        os.remove(temp_acc_file)
                    except:
                        pass  # If we can't remove it, continue anyway
                
                # register komutu --accept-tos ile ve pencere gizli
                proc = subprocess.Popen(
                    [wgcf_path, "register", "--accept-tos"],
                    cwd=res_dir,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    creationflags=subprocess.CREATE_NO_WINDOW
                )
                stdout, stderr = proc.communicate()

                if proc.returncode != 0:
                    error_msg = stderr.decode() if stderr else "Unknown error"
                    raise Exception(f"Register işlemi başarısız oldu. Hata: {error_msg}")

                # generate komutu, pencere gizli
                result2 = subprocess.run(
                    [wgcf_path, "generate"],
                    cwd=res_dir,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    creationflags=subprocess.CREATE_NO_WINDOW
                )

                if result2.returncode != 0:
                    raise Exception(f"Generate işlemi başarısız oldu:\n{result2.stderr.decode()}")
                    hide_loading_gif()

                if not os.path.exists(temp_conf_path):
                    hide_loading_gif()
                    raise FileNotFoundError("wgcf-profile.conf dosyası oluşturulamadı.")

                final_conf_path = modify_and_rename_conf(folder_list)

                if final_conf_path:
                    hide_loading_gif()
                    messagebox.showinfo("Başarılı", f".conf dosyası oluşturuldu:\n{final_conf_path}")
                else:
                    hide_loading_gif()
                    messagebox.showerror("Hata", ".conf dosyası oluşturulamadı.")

            except Exception as e:
                hide_loading_gif()
                messagebox.showerror("Hata", f".conf dosyası oluşturulurken hata:\n{e}")
        threading.Thread(target=run).start()
        hide_loading_gif()




    # Gelişmiş Ayarlar Sayfası UI Öğeleri
    button_row = tk.Frame(settings_frame, bg="#FFFFFF")
    button_row.pack(anchor="w", padx=10, pady=(10, 5))
    
    def clear_folder_list():
        folder_list.clear()
        update_folder_listbox()
    
    ttk.Button(button_row, text="Klasör Ekle", command=add_folder).pack(side="left", padx=(0, 5))
    ttk.Button(button_row, text="Listeyi Temizle", command=lambda: clear_folder_list()).pack(side="left")

    folder_list_container = tk.Frame(settings_frame, bg="#D9D9D9")
    folder_list_container.pack(fill="x", expand=False, padx=10, pady=(0, 10))

    canvas = tk.Canvas(folder_list_container, height=100, bg="#E6E6E6", highlightthickness=2)
    scrollbar = ttk.Scrollbar(folder_list_container, orient="vertical", command=canvas.yview)
    folder_list_frame = tk.Frame(canvas, bg="#E6E6E6")

    folder_list_frame.bind("<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
    canvas.create_window((0, 0), window=folder_list_frame, anchor="nw")
    canvas.configure(yscrollcommand=scrollbar.set)

    canvas.pack(side="left", fill="both", expand=True)
    scrollbar.pack(side="right", fill="y")

    ttk.Button(settings_frame, text="Özelleştirilmiş Hızlı Kurulum", command=run_custom_setup).pack(pady=5)
    ttk.Button(settings_frame, text="Özelleştirilmiş Profil Dosyası Oluştur", command=generate_custom_conf_only).pack(pady=(5, 0))
    ttk.Button(settings_frame, text="WireSock Hizmetini Kaldır", command=remove_wiresock_service).pack(pady=(95, 10))
    back_button = ttk.Button(settings_frame, text="← Geri", command=lambda: show_frame(main_frame))
    back_button.pack()



    update_wiresock_ui()
    show_frame(main_frame)
    root.mainloop()
    
if __name__ == "__main__":
    main_gui()