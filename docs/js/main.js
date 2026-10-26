"use strict";
const menuButton = document.querySelector('.menu-toggle');
const navigation = document.getElementById('site-links');
menuButton?.addEventListener('click', () => {
  const expanded = menuButton.getAttribute('aria-expanded') === 'true';
  menuButton.setAttribute('aria-expanded', String(!expanded));
  navigation.classList.toggle('is-open', !expanded);
});
const sectionLinks = [...(navigation?.querySelectorAll('a[href^="#"]') || [])];
function markActiveSection(hash) {
  const target = sectionLinks.find(link => link.getAttribute('href') === hash);
  if (!target) return;
  sectionLinks.forEach(link => {
    const active = link === target;
    link.classList.toggle('active', active);
    if (active) link.setAttribute('aria-current', 'location');
    else link.removeAttribute('aria-current');
  });
}
for (const link of sectionLinks) {
  link.addEventListener('click', () => {
    // Keep the selected section highlighted when the mobile menu is reopened.
    markActiveSection(link.getAttribute('href'));
    navigation.classList.remove('is-open');
    menuButton?.setAttribute('aria-expanded', 'false');
  });
}
window.addEventListener('hashchange', () => markActiveSection(window.location.hash));
markActiveSection(window.location.hash || '#home');

(() => {
  const triggers = [...document.querySelectorAll('.book-trigger, [data-lantern-detail], .footer-imprint-trigger')];
  const dialog = document.getElementById('book-dialog');
  if (!triggers.length || !dialog) return;

  const image = dialog.querySelector('.book-animation-frame');
  const panel = dialog.querySelector('.book-feature-panel');
  const content = dialog.querySelector('.book-dialog-content');
  const closeButton = dialog.querySelector('.book-dialog-close');
  const titleNode = dialog.querySelector('#book-dialog-title');
  const subtitleNode = dialog.querySelector('.book-panel-subtitle');
  const columnSections = dialog.querySelectorAll('.book-panel-columns section');
  const noteNode = dialog.querySelector('.book-panel-note');

  const playerGuideColumns = [[["Audiobookshelf", "Web · Server · Android · iPhone · iPad", "Excellent audiobook support with embedded chapters, cover art, series information, and extensive metadata. Although Audiobookshelf has a dedicated Narrator field, it reads narrator information from the Composer tag rather than the separate Narrator tag in audio files. BookStitch's optional compatibility mapping addresses this difference.\n\nAudiobookshelf also supports embedded Series and Series-Part metadata, allowing audiobooks to be grouped, organized, and sorted by series and sequence number. Metadata priorities may affect which information is displayed."], ["Plex", "Web · Windows · macOS · Linux · Android · iPhone · iPad", "M4B audio playback is supported, but Plex and Plexamp do not natively provide navigation through embedded audiobook chapters, whether Nero or QuickTime. Native audiobook metadata support is also limited. Neither the dedicated Narrator tag nor the Composer tag is treated as an audiobook narrator field.\n\nPlex does not natively recognize embedded audiobook series names or sequence numbers. Series organization requires alternative methods, such as manually managed collections. Third-party players that connect to Plex, such as Prologue, provide more advanced audiobook features, including chapter navigation."], ["Apple Books", "macOS · iPhone · iPad", "Supports audiobook playback with chapters and cover art. Personal audiobooks can be imported into Apple Books on a Mac. However, specialized metadata such as ISBN and ASIN may not appear as separate fields for imported audiobooks.\n\nApple Books supports series and sequence numbers for titles published through the Apple Books Store, but automatic recognition of embedded Series and Series-Part metadata in personally imported M4B files has not been confirmed."], ["Prologue", "iPhone · iPad · Android", "A dedicated audiobook player for Plex and Audiobookshelf libraries. Supports chapter navigation, cover art, series, bookmarks, and offline listening. Requires a connected Plex or Audiobookshelf server. Metadata changes may require a library refresh before they appear."]], [["Smart AudioBook Player", "Android", "Designed for locally stored audiobooks, including M4B files with excellent chapter navigation. Offers playback speed control, bookmarks, and individual listening progress. Each audiobook must be stored in its own folder.\n\nMetadata display is limited to basic information such as title, author, and genre. Dedicated Narrator tags and most extended metadata fields are not displayed. Embedded series names and sequence numbers are also not recognized or displayed as separate metadata fields."], ["BookPlayer", "iPhone · iPad", "Open-source audiobook player supporting local file imports, chapter navigation, bookmarks, and playback speed control. Files can be imported through AirDrop or the Files app. Some chapter-display issues have been reported with particular M4B files.\n\nNative recognition and display of embedded Series and Series-Part metadata has not been confirmed."], ["Voice Audiobook Player", "Android", "Open-source player supporting M4A, M4B, MP3, and other audio formats. Provides chapter navigation, embedded cover art, bookmarks, and playback progress. Search supports titles and authors, although specialized metadata fields are not all exposed."], ["VLC Media Player", "Windows · macOS · Linux · Android · iPhone · iPad", "Plays M4A and M4B files and supports chapter navigation. However, when both Nero and QuickTime chapter lists are present, VLC may prioritize the Nero chapters. This can limit navigation to 255 chapters, even when the QuickTime chapter track contains more.\n\nBookStitch avoids this conflict by exporting new audiobooks with QuickTime chapters only."]]];
  const BOOK_DETAILS = {
    'open-imprint': {
      title: 'Imprint',
      subtitle: 'Information pursuant to Section 18 (1) of the German Interstate Media Treaty (MStV)',
      sections: [
        {heading: 'Responsible for this website', body: 'Sascha Bärtschi\nHans-Thoma-Straße 6\n14467 Potsdam\nGermany'},
        {heading: 'GitHub', body: 'github.com/irwitzer', subsections: [
          {heading: 'About this project', body: 'BookStitch is a private, non-commercial open-source project. The software is provided free of charge and is licensed under the GNU General Public License, Version 3 (GPL-3.0).'}
        ]}
      ],
      note: ''
    },
    'open-formats-book': {"title": "Formats", "subtitle": "Choose the audio sources that fit your collection.", "sections": [{"heading": "Supported Input Formats", "blocks": [{"text": "BookStitch supports the following audio formats:"}, {"text": "MP3 · M4A · M4B · AAC · WAV · FLAC · OGG · OGA · Opus · WMA (Legacy)", "strong": true}]}, {"heading": "Supported Output Formats", "blocks": [{"text": "Export your audiobook as AAC audio in an M4A or M4B container, with flexible quality presets."}, {"label": "Output:", "text": "M4A · M4B (AAC)"}, {"label": "Presets:", "text": "64 kbps (Mono) · 96 kbps (Mono) · 96 kbps · 128 kbps · 160 kbps · 192 kbps · 256 kbps"}]}], "note": ""},
    'open-chapter-book': {"title": "Chapter", "subtitle": "Shape clear chapters for a better listening experience.", "sections": [{"heading": "Smart Chapter Creation", "body": "Automatically detect and preserve embedded chapters, or generate chapter suggestions from track titles, tags, and file names. Seamlessly combine files with different chapter structures into one audiobook, while keeping everything in the correct order."}, {"heading": "Custom Chapter Control", "body": "Take full control of your chapters with customizable titles, flexible naming patterns, and automatic numbering. Keep existing chapters, refine suggested ones, or create your own. BookStitch precisely calculates chapter positions and timing across all tracks for a seamless listening experience."}], "note": ""},
    'open-metadata-book': {"title": "Metadata & Cover Art", "subtitle": "Give your audiobook the details and artwork it deserves.", "sections": [{"heading": "Rich Audiobook Metadata", "body": "Keep every detail of your audiobook organized. Review and edit titles, authors, narrators, series information, publishers, languages, ISBN, ASIN, and more. Multi-value metadata fields support multiple authors, narrators, genres, and other contributors. BookStitch reads existing tags from your source files and carries your chosen metadata into the finished audiobook, keeping everything consistent and neatly organized."}, {"heading": "Cover Art Integration", "body": "Give your audiobook the perfect finishing touch. Import cover images in JPEG, PNG, or WebP format and let BookStitch automatically prepare them for a clean, consistent appearance. Intelligent image handling creates square artwork when needed, while built-in quality checks help you identify covers with insufficient resolution before exporting."}], "note": ""},
    'open-tracks-book': {"title": "Smart Track Management", "subtitle": "Keep your tracks organized, reviewed, and under control.", "sections": [{"heading": "Intelligent Track Organization", "body": "Bring order to even the largest audio collections. BookStitch automatically organizes tracks using file names, track numbers, metadata, and disc information. Smart detection highlights duplicates, potential gaps, and uncertain track sequences, helping you identify problems early and keep your audiobook complete and properly arranged."}, {"heading": "Full Track Control", "body": "Stay in control of every track from import to export. Easily rearrange tracks, adjust titles, or exclude unwanted files without modifying your original audio. Review helpful warnings, fine-tune the order, and make sure everything is exactly where it belongs before creating your finished audiobook."}], "note": ""},
    'open-passthrough-book': {"title": "Pass Through", "subtitle": "Preserve compatible audio without unnecessary re-encoding.", "sections": [{"heading": "Preserve Original Audio Quality", "body": "Why re-encode audio when you don't have to? BookStitch can reuse compatible AAC audio directly, preserving the original encoded data without unnecessary conversion or additional quality loss. This saves processing time while keeping your audio exactly as it was encoded."}, {"heading": "Intelligent Audio Processing", "body": "BookStitch automatically checks codecs, bitrates, channels, and sample rates to determine when Pass-Through is possible. Compatible AAC tracks are preserved, while others are converted when needed.\n\nPass-Through supports sample rates of 16.000 · 22.050 · 24.000 · 32.000 · 44.100 · 48.000 · 64.000 · 88.200 · 96.000 Hz, provided the audio and export settings are compatible.\n\nAll tracks are prepared for a seamless M4A or M4B audiobook."}], "note": ""},
    'open-drive-circle-book': {"title": "Drive Circle", "subtitle": "Keep multi-disc imports organized from drive to drive.", "sections": [{"heading": "Multiple Drives, One Workflow", "body": "Take the hassle out of importing large audiobook collections. Drive Circle lets you use up to five optical drives, automatically processing discs one after another. Whether you're working with audio CDs or MP3 CDs, BookStitch keeps everything organized and reduces the need for constant disc changes."}, {"heading": "Smart Disc Recognition", "body": "BookStitch automatically identifies inserted discs and keeps track of those already processed. Intelligent detection helps prevent duplicate imports, recognizes incorrect disc types, and guides you through disc changes. Even complex multi-disc collections become a smooth, organized workflow from the first disc to the last."}], "note": ""},
    'open-project-book': {"title": "Project", "subtitle": "Keep your projects ready to revisit, refine, and re-export.", "sections": [{"heading": "Your Audiobook Project Library", "body": "Keep all your audiobook projects organized and accessible in one place. BookStitch preserves your project data, including tracks, chapters, metadata, and export settings, so you can return to your work whenever needed. Projects are retained for 180 days by default, with a configurable retention period of up to 360 days. Plenty of time to review, revisit, and perfect your audiobook collection."}, {"heading": "Review, Refine & Re-Export", "body": "Test your finished audiobook in your favorite player, whether it's Plex, Audiobookshelf, Smart AudioBook Player, or Apple Books. Want to change something? Simply reopen your project, update the metadata, choose a different audio quality preset, and export again without starting from scratch. Pause and resume your work as needed, keeping your projects ready for adjustments until you're completely satisfied with the result."}], "note": ""},
    'open-shadow-lantern': {
      "title": "Zero Bullshit",
      "subtitle": "Your Files. Your Rules. No Bullshit.",
      "sections": [
        {
          "heading": "Your Files Stay Yours",
          "body": "BookStitch only reads your original audio files. It never modifies, renames, moves, deletes, or overwrites them. All conversion, editing, and export operations use separate project and output files. Your original collection remains untouched and protected.",
          "extraHeading": "Privacy by Design",
          "extraBody": "No account, no sign-in, no advertising, no tracking, no telemetry, and no subscriptions. BookStitch does not collect usage data or upload your audio files, metadata, cover art, or project information. Your audiobook projects stay on your own computer."
        },
        {
          "heading": "Offline & Transparent Processing",
          "body": "BookStitch uses conventional audio-processing algorithms powered by FFmpeg and FFprobe. Core audiobook processing requires neither artificial intelligence nor cloud services. Once the required tools are installed, audiobook creation works entirely offline. Internet access is only needed for activities such as downloading tools, installing updates, or opening external links.",
          "extraHeading": "Open Source, Open Formats",
          "extraBody": "BookStitch is open-source software licensed under GNU GPL v3.0-only. Its source code is publicly available for inspection, modification, and redistribution under the license terms. Finished audiobooks use standard M4A or M4B containers with AAC audio, without proprietary file formats or vendor lock-in."
        }
      ],
      "note": ""
    },
    'open-antique-projector': {
    "title": "Player Compatibility",
    "subtitle": "Your audiobook. Your player. Your choice.",
    "sections": [
        {
            "heading": "Your Player, Your Choice",
            "body": "Your finished audiobook deserves a player that handles it properly. M4A and M4B files are supported by many applications, but chapter navigation, embedded cover art, and metadata display can vary between players. Some applications recognize detailed audiobook metadata, while others only support a limited selection of fields.\n\nBookStitch takes these differences into account and includes several compatibility features to help keep your audiobook information accessible across different players.",
            "subsections": [
                {
                    "heading": "Compatibility Metadata",
                    "body": "Not every audiobook player recognizes dedicated metadata fields for narrators, series information, or other book details. To improve compatibility, BookStitch can include essential information such as the title, author, narrator, series, language, duration, and chapter count in the standard Description field.\n\nEven when a player ignores specialized metadata, this information may still be available in the book description. Some applications also include descriptions in their search functionality, making it easier to find audiobooks by details that would otherwise remain hidden.\n\nThis compatibility feature can be disabled in BookStitch's metadata settings."
                },
                {
                    "heading": "Narrator & Composer Compatibility",
                    "body": "The Composer field was originally intended for music, but some audiobook players use it to identify the narrator instead.\n\nBookStitch supports this convention by automatically copying narrator information into the Composer field while preserving the dedicated narrator metadata. This improves compatibility with applications that do not recognize the narrator field directly.\n\nThe option is enabled by default and can be turned off at any time. Your original composer information remains available when the compatibility mapping is disabled."
                }
            ]
        },
        {
            "heading": "Reliable Chapter Navigation",
            "body": "BookStitch supports existing embedded chapters, including older Nero chapter markers, and combines them with chapters generated from your source files.\n\nFinished audiobooks use QuickTime chapter markers, avoiding the 255-chapter limitation of the older Nero format. Chapter positions are calculated across the complete audiobook, even when source files contain different chapter structures.\n\nThis provides a consistent chapter structure for players that support QuickTime chapters, including audiobooks with hundreds of chapter markers.",
            "subsections": [
                {
                    "heading": "Test Your Favorite Player",
                    "body": "Want to know how your player handles audiobook files? Download our compatibility samples and check the results yourself.\n\nExplore chapter navigation, embedded cover art, author and narrator information, series metadata, and the compatibility description directly in your preferred application.\n\nDifferent players may display the same audiobook differently. Our compatibility overview highlights known limitations and explains what to expect."
                }
            ]
        }
    ],
    "note": ""
},
    'open-shadow-projector': { title:'Player Compatibility Guide', subtitle:'How different players handle your audiobooks.', sections:[{heading:'Audiobookshelf',body:''},{heading:'Smart AudioBook Player',body:''}],note:'' },
    'open-export-book': {"title": "M4A & M4B Export", "subtitle": "Export your finished audiobook in the format you need.", "sections": [{"heading": "One Complete Audiobook", "body": "Turn hundreds of individual audio tracks into a single, beautifully organized audiobook. BookStitch brings everything together in an M4A or M4B file, complete with chapter markers, embedded cover art, and carefully prepared metadata. One complete audiobook, ready for your collection."}, {"heading": "Flexible, High-Quality Export", "body": "Choose the format and audio quality that best suit your needs. BookStitch offers flexible AAC encoding presets ranging from 64 kbps mono to 256 kbps stereo. Audio tracks are carefully prepared and combined, with chapter timestamps verified to within 0.5 milliseconds in testing and consistent metadata throughout your finished audiobook."}], "note": ""},
  };

  syncOverviewCatchphrases();

  let active = false;
  let ghost = null;
  let animation = null;
  let focusBefore = null;
  let activeTrigger = null;
  let serial = 0;
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  const targetSize = () => Math.min(window.innerWidth * 0.32, window.innerHeight * 0.26, 215);

  function syncOverviewCatchphrases() {
    triggers.forEach(trigger => {
      const details = BOOK_DETAILS[trigger.id];
      const desc = trigger.querySelector('.book-trigger-desc');
      if (details && desc) desc.textContent = details.subtitle;
    });
  }

  function setPanelCopy(triggerId) {
    const details = BOOK_DETAILS[triggerId] || BOOK_DETAILS['open-formats-book'];
    titleNode.textContent = details.title;
    subtitleNode.textContent = details.subtitle;
    details.sections.forEach((section, index) => {
      const container = columnSections[index];
      if (!container) return;
      // All feature text is inserted as text nodes, not untrusted HTML.
      container.replaceChildren();
      const heading = document.createElement('h3');
      heading.textContent = section.heading;
      container.appendChild(heading);
      for (const block of section.blocks || [{ text: section.body }]) {
        const paragraphs = block.text.split(/\n\n/);
        for (const paragraphText of paragraphs) {
        const paragraph = document.createElement('p');
        if (block.label) {
          const label = document.createElement('strong');
          label.textContent = block.label + ' ';
          paragraph.appendChild(label);
        }
        if (block.strong) {
          const strong = document.createElement('strong');
          strong.textContent = paragraphText;
          paragraph.appendChild(strong);
        } else {
          paragraph.appendChild(document.createTextNode(paragraphText));
        }
        if (triggerId === 'open-imprint' && index === 0) paragraph.classList.add('imprint-address');
        container.appendChild(paragraph);
        }
      }
      // V89: contextual shortcuts to the nested download-samples view.
      // The link uses the same deep-link handler as an external #download-samples URL.
      const sampleLinkColumn = {
        'open-chapter-book': 0,
        'open-metadata-book': 1,
        'open-export-book': 0
      };
      if (sampleLinkColumn[triggerId] === index) {
        const sampleLink = document.createElement('a');
        sampleLink.href = '#download-samples';
        sampleLink.className = 'book-sample-shortcut';
        sampleLink.textContent = 'Download BookStitch Sample Audiobook';
        sampleLink.setAttribute('aria-label', 'Open Download Samples');
        container.appendChild(sampleLink);
      }
      if (triggerId === 'open-imprint' && index === 1) {
        const githubParagraph = container.querySelector('p');
        if (githubParagraph) {
          const link = document.createElement('a');
          link.href = 'https://github.com/irwitzer';
          link.target = '_blank';
          link.rel = 'noopener noreferrer';
          link.textContent = 'https://github.com/irwitzer';
          githubParagraph.replaceChildren(link);
        }
      }
      // Optional additional headings and paragraphs for longer detail panels.
      for (const part of section.subsections || []) {
        const subheading = document.createElement('h3');
        subheading.textContent = part.heading;
        subheading.className = 'lantern-extra-heading';
        container.appendChild(subheading);
        if (part.body) {
          for (const paragraphText of part.body.split(/\n\n/)) {
            const paragraph = document.createElement('p');
            paragraph.textContent = paragraphText;
            container.appendChild(paragraph);
          }
        }
      }
      if (section.extraHeading && section.extraBody) {
        const extraTitle = document.createElement('h3');
        extraTitle.textContent = section.extraHeading;
        extraTitle.className = 'lantern-extra-heading';
        const extraPara = document.createElement('p');
        extraPara.textContent = section.extraBody;
        container.append(extraTitle, extraPara);
      }
    });
    if (triggerId === 'open-antique-projector') {
      const link = document.createElement('a');
      link.href = '#download-samples';
      link.className = 'player-samples-link';
      link.textContent = 'Download Samples';
      columnSections[1].appendChild(link);
    }
    if (triggerId === 'open-shadow-projector') {
      columnSections.forEach((section, index) => {
        section.replaceChildren();
        playerGuideColumns[index].forEach(([heading,platforms,body]) => {
          const h = document.createElement('h3'); h.textContent = heading;
          const platform = document.createElement('p');
          platform.className = 'player-guide-platforms'; platform.textContent = platforms;
          section.append(h,platform);
          body.split(/\n\n/).forEach(text => {const p=document.createElement('p'); p.textContent=text;section.append(p);});
        });
      });
      const notice = document.createElement('section'); notice.className = 'player-guide-notice';
      const heading = document.createElement('h3'); heading.textContent = 'Compatibility Notice';
      notice.appendChild(heading);
      const paragraphs = [
        'This information is based on official documentation, published reports, and hands-on testing of several players. Player compatibility can change as applications receive updates, fixing existing issues or occasionally introducing new ones.',
        'The best way to check compatibility is to try it yourself. Download our sample audiobooks and see how your preferred player handles chapters, cover art, and metadata.'
      ];
      paragraphs.forEach(text=>{const p=document.createElement('p');p.textContent=text;notice.appendChild(p);});
      const guide = document.createElement('button'); guide.type='button';
      guide.className='gramophone-guide-trigger'; guide.setAttribute('aria-label','Open the gramophone details');
      const art=document.createElement('img'); art.src='assets/gramophone-compatibility-v72.webp';art.alt='';
      art.width=1164;art.height=1330;
      const caption=document.createElement('span');caption.textContent='Download Samples';
      guide.append(art,caption);
      noteNode.replaceChildren(notice, guide);
    } else {
      noteNode.textContent = details.note;
    }
  }

  function clearGhost() {
    if (animation) {
      animation.cancel();
      animation = null;
    }
    if (ghost) {
      ghost.remove();
      ghost = null;
    }
    if (activeTrigger) {
      const sourceImg = activeTrigger.querySelector('.step-icon-book img, .lantern-visual img, .footer-star-wrap img');
      if (sourceImg) sourceImg.style.visibility = '';
    }
  }

  // V83 — stable deep links for the eight books, four Magic devices and nested samples.
  const DETAILS_BY_HASH = Object.freeze({
    formats:'open-formats-book', chapter:'open-chapter-book',
    metadata:'open-metadata-book', 'smart-track-management':'open-tracks-book',
    'pass-through':'open-passthrough-book', 'drive-circle':'open-drive-circle-book',
    project:'open-project-book', export:'open-export-book',
    'shadow-lantern':'open-shadow-lantern',
    'antique-projector':'open-antique-projector',
    'shadow-projector':'open-shadow-projector'
  });
  const HASH_BY_TRIGGER = Object.fromEntries(Object.entries(DETAILS_BY_HASH).map(([hash,id])=>[id,'#'+hash]));
  function hashForTrigger(id) { return HASH_BY_TRIGGER[id] || '#imprint'; }
  function detailHash() {
    const key = decodeURIComponent(location.hash.slice(1)).toLowerCase();
    return key === 'download-samples' || key === 'imprint' || Object.hasOwn(DETAILS_BY_HASH,key);
  }
  function setDetailHash(hash) {
    if (location.hash === hash) return;
    history.pushState({bookStitchDetail:true},'',hash);
    markActiveSection(hash === '#imprint' ? '#home' : (hash === '#download-samples' || hash.includes('lantern') || hash.includes('projector') ? '#magic' : '#features'));
  }
  function navigateToDefault(trigger) {
    const target = trigger?.hasAttribute('data-lantern-detail') ? '#magic'
      : trigger?.id === 'open-imprint' ? '#home' : '#features';
    history.replaceState(null,'',target);
    markActiveSection(target);
  }
  let syncingUrl = false;
  async function syncDetailsFromUrl() {
    if (syncingUrl) return;
    syncingUrl = true;
    try {
      const key = decodeURIComponent(location.hash.slice(1)).toLowerCase();
      const nested = key === 'download-samples';
      if (key === 'magic-lantern') {
        if (guideActive) closeGuide({keepHash:true});
        if (active) close({keepHash:true});
        return; // The screenshot gallery maintains its own deep-link handler.
      }
      const id = nested ? DETAILS_BY_HASH['shadow-projector'] : key === 'imprint' ? 'open-imprint' : DETAILS_BY_HASH[key];
      if (!id) {
        if (guideActive) closeGuide({keepHash:true});
        if (active) close({keepHash:true});
        return;
      }
      const trigger = document.getElementById(id);
      if (!trigger) return;
      if (guideActive && !nested) closeGuide({keepHash:true});
      if (active && activeTrigger?.id !== id) close({keepHash:true});
      if (!active) await open(trigger,{fromUrl:true,instant:true});
      if (nested && !guideActive) {
        const gramophone = dialog.querySelector('.gramophone-guide-trigger');
        if (gramophone) await openGuide(gramophone,{fromUrl:true,instant:true});
      }
      markActiveSection(id.includes('book') ? '#features' : id === 'open-imprint' ? '#home' : '#magic');
    } finally { syncingUrl = false; }
  }
  function close(options = {}) {
    if (!options.keepHash && detailHash()) navigateToDefault(activeTrigger);
    serial++;
    clearGhost();
    active = false;
    dialog.hidden = true;
    dialog.setAttribute('aria-hidden', 'true');
    dialog.classList.remove('is-ready', 'is-lantern-detail', 'is-imprint-detail');
    panel.setAttribute('aria-hidden', 'true');
    document.body.classList.remove('book-dialog-visible');
    activeTrigger = null;
    if (focusBefore?.isConnected) focusBefore.focus();
  }

  async function open(trigger, options = {}) {
    if (active) return;
    active = true;
    activeTrigger = trigger;
    const token = ++serial;
    focusBefore = options.fromUrl ? null : document.activeElement;
    if (!options.fromUrl) setDetailHash(hashForTrigger(trigger.id));
    const source = trigger.querySelector('.step-icon-book img, .lantern-visual img, .footer-star-wrap img');
    const bookSrc = source?.currentSrc || source?.src;
    if (!bookSrc || !source) { active = false; return; }

    dialog.dataset.detailTrigger = trigger.id;
    dialog.classList.toggle('is-lantern-detail', trigger.hasAttribute('data-lantern-detail') || trigger.id === 'open-imprint');
    dialog.classList.toggle('is-imprint-detail', trigger.id === 'open-imprint');
    setPanelCopy(trigger.id);
    const detailScroller = panel.querySelector('.book-panel-scroll');
    detailScroller.scrollTop = 0;
    image.src = bookSrc;
    image.alt = '';
    dialog.hidden = false;
    dialog.setAttribute('aria-hidden', 'false');
    detailScroller.scrollTop = 0;
    dialog.classList.remove('is-ready');
    panel.setAttribute('aria-hidden', 'true');
    document.body.classList.add('book-dialog-visible');
    dialog.style.setProperty('--detail-book-size', targetSize() + 'px');
    content.focus();

    if (!options.instant && !reducedMotion.matches) {
      const original = source.getBoundingClientRect();
      ghost = document.createElement('img');
      ghost.src = bookSrc;
      ghost.alt = '';
      ghost.setAttribute('aria-hidden', 'true');
      Object.assign(ghost.style, {
        position: 'fixed',
        left: original.left + 'px',
        top: original.top + 'px',
        width: original.width + 'px',
        height: original.height + 'px',
        objectFit: 'contain',
        pointerEvents: 'none',
        zIndex: '1002',
        transformOrigin: 'center center',
        filter: 'drop-shadow(0 16px 34px rgba(0,0,0,.58))',
      });
      document.body.appendChild(ghost);
      source.style.visibility = 'hidden';
      const landing = image.getBoundingClientRect();
      const dx = landing.left - original.left;
      const dy = landing.top - original.top;
      const sx = landing.width / original.width;
      const sy = landing.height / original.height;
      animation = ghost.animate(
        [
          { transform: 'translate(0,0) scale(1)', opacity: 1 },
          { transform: `translate(${dx}px,${dy}px) scale(${sx},${sy})`, opacity: 1 }
        ],
        { duration: 820, easing: 'cubic-bezier(.23,.78,.25,1)', fill: 'forwards' }
      );
      try {
        await animation.finished;
      } catch (_) {
        return;
      }
      if (serial !== token) return;
      clearGhost();
    }

    if (serial !== token) return;
    dialog.classList.add('is-ready');
    panel.setAttribute('aria-hidden', 'false');
    detailScroller.scrollTop = 0;
    closeButton.focus({preventScroll: true});
  }

  // V72: Nested compatibility guide, using a separate detail panel.
  // Closing it returns to the exact parent-panel scroll position.
  const guideDialog = dialog.cloneNode(true);
  guideDialog.id = 'gramophone-dialog';
  guideDialog.setAttribute('aria-labelledby', 'gramophone-dialog-title');
  guideDialog.classList.add('is-lantern-detail');
  guideDialog.querySelector('#book-dialog-title').id = 'gramophone-dialog-title';
  guideDialog.querySelector('.book-dialog-close').setAttribute('aria-label', 'Return to Player Compatibility Guide');
  guideDialog.querySelector('.book-animation-frame').src = 'assets/gramophone-compatibility-v72.webp';
  guideDialog.querySelector('.book-animation-frame').setAttribute('width', '1180');
  guideDialog.querySelector('.book-animation-frame').setAttribute('height', '1333');
  guideDialog.querySelector('#gramophone-dialog-title').textContent = 'Download Samples';
  guideDialog.querySelector('.book-panel-subtitle').textContent = 'Experience BookStitch in your favorite player.';

  const sampleColumns = guideDialog.querySelectorAll('.book-panel-columns section');
  sampleColumns.forEach(section => section.replaceChildren());
  function sampleHeading(parent, text, className = '') {
    const heading = document.createElement('h3');
    heading.textContent = text;
    if (className) heading.className = className;
    parent.appendChild(heading);
  }
  function sampleParagraph(parent, text, className = '') {
    const paragraph = document.createElement('p');
    paragraph.textContent = text;
    if (className) paragraph.className = className;
    parent.appendChild(paragraph);
  }
  // First column: Alice's Adventures in Wonderland reference sample (download URLs to follow).
  const alice = sampleColumns[0];
  sampleHeading(alice, "Alice's Adventures in Wonderland");
  sampleParagraph(alice, 'Lewis Carroll · Narrated by Kara Shallenberg · LibriVox Version 2', 'sample-byline');
  sampleParagraph(alice, 'A complete audiobook created with BookStitch from 12 original LibriVox audio tracks. The M4A file includes 12 embedded chapters, cover art, and extensive metadata, including authors, narrators, series information, publication details, and compatibility fields.');
  const aliceMulti = document.createElement('p');
  aliceMulti.append('This sample also includes ');
  const aliceMultiBold = document.createElement('strong');
  aliceMultiBold.textContent = 'multi-value metadata';
  aliceMulti.append(aliceMultiBold, ', with two authors, two narrators, three genres, and 14 separate keywords. Additional author and narrator entries are clearly marked as test values. BookStitch lets you arrange these values in your preferred order, while preserving the complete metadata. Some players display only the first value, even when multiple entries are stored in the file.');
  alice.appendChild(aliceMulti);
  sampleParagraph(alice, 'Download the audiobook to explore how your preferred player handles chapters, artwork, and metadata. The accompanying reference file lists the configured values, allowing you to compare them with what your player actually displays.');
  sampleHeading(alice, 'Audiobook Details', 'sample-subheading');
  const aliceFacts = [
    ['Title', "Alice's Adventures in Wonderland"],
    ['Author', 'Lewis Carroll (+ test value)'],
    ['Narrator', 'Kara Shallenberg (+ test value)'],
    ['Publisher', 'LibriVox'],
    ['Release Year', '2010'],
    ['Language', 'English'],
    ['Genres', "Audiobook · Fantasy · Children's Literature"],
    ['Series', 'Alice in Wonderland · Book 1'],
    ['Duration', '2h 47m 24s'],
    ['Chapters', '12'],
    ['Format', 'M4A · AAC Mono · 64 kbps'],
    ['File Size', '81.2 MB']
  ];
  const aliceFactsList = document.createElement('dl');
  aliceFactsList.className = 'sample-details-list';
  aliceFacts.forEach(([name,value]) => {
    const pair = document.createElement('div');
    const dt = document.createElement('dt'); dt.textContent = name + ':';
    const dd = document.createElement('dd'); dd.textContent = value;
    pair.append(dt,dd);
    aliceFactsList.appendChild(pair);
  });
  alice.appendChild(aliceFactsList);
  const aliceLinks = document.createElement('div');
  aliceLinks.className = 'sample-download-actions';
  [
    ['Download Audiobook (M4A)', 'https://github.com/irwitzer/BookStitch-Samples/releases/download/samples-v1/Lewis.Carroll.-.Alice.s.Adventures.in.Wonderland.m4a'],
    ['Download Full Metadata Reference (TXT)', 'https://github.com/irwitzer/BookStitch-Samples/releases/download/samples-v1/Lewis.Carroll.-.Alice.s.Adventures.in.Wonderland.Metadata-Reference.txt']
  ].forEach(([label, url]) => {
    const link = document.createElement('a');
    link.className = 'sample-download-link';
    link.href = url;
    link.textContent = label;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    aliceLinks.appendChild(link);
  });
  alice.appendChild(aliceLinks);
  sampleParagraph(alice, 'Some contributor entries and catalog identifiers are intentionally included for compatibility testing and do not represent actual contributors or identifiers of this specific recording. See the metadata reference for details.', 'sample-disclaimer');

  // Second column: Sherlock Holmes reference sample (links to follow).
  const sherlock = sampleColumns[1];
  sampleHeading(sherlock, 'The Adventures of Sherlock Holmes');
  sampleParagraph(sherlock, 'Arthur Conan Doyle · Narrated by StudioMike · LibriVox Version 5', 'sample-byline');
  sampleParagraph(sherlock, 'A complete audiobook created with BookStitch from 14 original LibriVox audio tracks. The finished M4B file contains 14 embedded chapters, cover art, and extensive metadata, including authors, narrators, series information, publication details, and compatibility fields.');
  const multi = document.createElement('p');
  multi.append('This sample also includes ');
  const multiBold = document.createElement('strong');
  multiBold.textContent = 'multi-value metadata';
  multi.append(multiBold, ', with two authors, two narrators, three genres, and 13 separate keywords. Additional author and narrator entries are clearly marked as test values. BookStitch lets you arrange these values in your preferred order, while preserving the complete metadata. Some players display only the first value, even when multiple entries are stored in the file.');
  sherlock.appendChild(multi);
  sampleParagraph(sherlock, 'Download the audiobook to explore how your preferred player handles chapters, artwork, and metadata. The accompanying reference file lists the configured values, allowing you to compare them with what your player actually displays.');
  sampleHeading(sherlock, 'Audiobook Details', 'sample-subheading');
  const sampleFacts = [
    ['Title', 'The Adventures of Sherlock Holmes'],
    ['Author', 'Arthur Conan Doyle (+ test value)'],
    ['Narrator', 'StudioMike (+ test value)'],
    ['Publisher', 'LibriVox'],
    ['Release Year', '2019'],
    ['Language', 'English'],
    ['Genres', 'Audiobook · Mystery · Detective Fiction'],
    ['Series', 'Sherlock Holmes · Book 3'],
    ['Duration', '9h 35m 41s'],
    ['Chapters', '14'],
    ['Format', 'M4B · AAC'],
    ['File Size', 'Approx. 400 MB']
  ];
  const facts = document.createElement('dl');
  facts.className = 'sample-details-list';
  sampleFacts.forEach(([name,value]) => {
    const pair = document.createElement('div');
    const dt = document.createElement('dt'); dt.textContent = name + ':';
    const dd = document.createElement('dd'); dd.textContent = value;
    pair.append(dt,dd);
    facts.appendChild(pair);
  });
  sherlock.appendChild(facts);
  const links = document.createElement('div');
  links.className = 'sample-download-actions';
  [
    ['Download Audiobook (M4B)', 'https://github.com/irwitzer/BookStitch-Samples/releases/download/samples-v1/Arthur.Conan.Doyle.-.The.Adventures.of.Sherlock.Holmes.m4b'],
    ['Download Full Metadata Reference (TXT)', 'https://github.com/irwitzer/BookStitch-Samples/releases/download/samples-v1/Arthur.Conan.Doyle.-.The.Adventures.of.Sherlock.Holmes.Metadata-Reference.txt']
  ].forEach(([label, url]) => {
    const link = document.createElement('a');
    link.className = 'sample-download-link';
    link.href = url;
    link.textContent = label;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    links.appendChild(link);
  });
  sherlock.appendChild(links);
  sampleParagraph(sherlock, 'Some contributor entries and catalog identifiers are intentionally included for compatibility testing and do not represent actual contributors or identifiers of this specific recording. See the metadata reference for details.', 'sample-disclaimer');
  guideDialog.querySelector('.book-panel-note').textContent = '';
  guideDialog.hidden = true;
  guideDialog.setAttribute('aria-hidden','true');
  dialog.after(guideDialog);
  const guideContent = guideDialog.querySelector('.book-dialog-content');
  let guideActive = false;
  let guideReturnFocus = null;
  function closeGuide(options = {}) {
    if (!options.keepHash) setDetailHash('#shadow-projector');
    if (!guideActive) return;
    guideActive = false;
    guideDialog.hidden = true;
    guideDialog.classList.remove('is-ready');
    guideDialog.setAttribute('aria-hidden','true');
    dialog.removeAttribute('aria-hidden');
    if (guideReturnFocus?.isConnected) guideReturnFocus.focus({preventScroll:true});
  }
  async function openGuide(trigger, options = {}) {
    if (!trigger || guideActive) return;
    if (!options.fromUrl) setDetailHash('#download-samples');
    guideActive = true;
    guideReturnFocus = trigger;
    dialog.setAttribute('aria-hidden','true');
    guideDialog.hidden = false;
    guideDialog.querySelector('.book-panel-scroll').scrollTop = 0;
    guideDialog.classList.remove('is-ready');
    guideDialog.setAttribute('aria-hidden','false');
    guideDialog.style.setProperty('--detail-book-size', targetSize()+'px');
    guideContent.focus({preventScroll:true});
    const source = trigger.querySelector('img');
    const destination = guideDialog.querySelector('.book-animation-frame');
    if (!options.instant && !reducedMotion.matches && source?.animate) {
      const from = source.getBoundingClientRect();
      const to = destination.getBoundingClientRect();
      // Position-preserving image flight, as used for feature books.
      const flying = source.cloneNode();
      Object.assign(flying.style,{
        position:'fixed',left:from.left+'px',top:from.top+'px',
        width:from.width+'px',height:from.height+'px',
        objectFit:'contain',zIndex:'1102',pointerEvents:'none',
        transformOrigin:'top left'
      });
      document.body.append(flying);
      const a = flying.animate([
        {transform:'translate(0,0) scale(1)'},
        {transform:`translate(${to.left-from.left}px,${to.top-from.top}px) scale(${to.width/from.width},${to.height/from.height})`}
      ],{duration:550,easing:'cubic-bezier(.23,.78,.25,1)'});
      try { await a.finished; } catch (_) {}
      flying.remove();
    }
    if (guideActive) {
      guideDialog.classList.add('is-ready');
      guideDialog.querySelector('.book-panel-scroll').scrollTop = 0;
    }
    guideDialog.querySelector('.book-dialog-close').focus({preventScroll:true});
  }
  dialog.addEventListener('click', event => {
    const trigger = event.target.closest('.gramophone-guide-trigger');
    if (trigger) void openGuide(trigger);
  });
  guideDialog.querySelectorAll('[data-book-close]').forEach(node=>node.addEventListener('click',closeGuide));
  document.addEventListener('keydown',event=>{
    if (guideActive && event.key==='Escape') {
      event.preventDefault();
      event.stopImmediatePropagation();
      closeGuide();
    }
  },true);
  window.addEventListener('resize',()=>{
    if (guideActive) guideDialog.style.setProperty('--detail-book-size',targetSize()+'px');
  });

  triggers.forEach(trigger => trigger.addEventListener('click', () => open(trigger)));
  window.addEventListener('popstate', () => { void syncDetailsFromUrl(); });
  window.addEventListener('hashchange', () => { void syncDetailsFromUrl(); });
  void syncDetailsFromUrl();
  dialog.querySelectorAll('[data-book-close]').forEach(node => node.addEventListener('click', close));
  window.addEventListener('resize', () => {
    if (!dialog.hidden) dialog.style.setProperty('--detail-book-size', targetSize() + 'px');
  });
  document.addEventListener('keydown', event => {
    if (!active) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      close();
    }
  });
})();

// V36: randomly timed glow groups remain, and overview catchphrases are synced to the popup subtitles.
(() => {
  const books = [...document.querySelectorAll('.workflow .book-trigger')];
  if (!books.length) return;
  const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');
  const active = new Set();
  const pulseDuration = 1450;
  let timer;

  function pulse() {
    if (!document.hidden && !document.body.classList.contains('book-dialog-visible') && !motionPreference.matches) {
      const available = books.filter(book => !active.has(book));
      // Usually one or two, sometimes three, rarely four at the same time.
      const roll = Math.random();
      const requested = roll < .47 ? 1 : roll < .82 ? 2 : roll < .96 ? 3 : 4;
      const count = Math.min(requested, available.length, 4 - active.size);
      for (let i = 0; i < count; i++) {
        const index = Math.floor(Math.random() * available.length);
        const book = available.splice(index, 1)[0];
        active.add(book);
        book.classList.add('is-pulsing');
        window.setTimeout(() => {
          book.classList.remove('is-pulsing');
          active.delete(book);
        }, pulseDuration);
      }
    }
    // Slower and nonperiodic compared with V34's fixed one-second rotation.
    timer = window.setTimeout(pulse, 1900 + Math.random() * 1600);
  }
  timer = window.setTimeout(pulse, 1300 + Math.random() * 1600);
  motionPreference.addEventListener?.('change', () => {
    if (motionPreference.matches) {
      for (const book of active) book.classList.remove('is-pulsing');
      active.clear();
    }
  });
})();


// V51: Three Magic triggers all reveal the screenshot gallery.
(() => {
  const triggers=[...document.querySelectorAll('[data-gallery-trigger]')];
  const dialog=document.querySelector('#gallery-dialog');
  if(!triggers.length||!dialog)return;
  const slides=[
    {title:'Main Window — Sherlock Holmes',desc:'Sherlock Holmes audiobook prepared in BookStitch.',src:'assets/screenshot-01-v86.webp',alt:'Sherlock Holmes audiobook prepared in BookStitch.',width:1251,height:861},
    {title:'Metadata — Sherlock Holmes',desc:'Sherlock Holmes multi-value metadata in the editor.',src:'assets/screenshot-02-v86.webp',alt:'Sherlock Holmes multi-value metadata in the editor.',width:1252,height:860},
    {title:'Drive Circle Settings',desc:'Drive Circle settings with connected optical drives.',src:'assets/screenshot-03-v86.webp',alt:'Drive Circle settings with connected optical drives.',width:1254,height:862},
    {title:'Project Library — Alice in Wonderland',desc:'Resume or manage the Alice in Wonderland audiobook project.',src:'assets/screenshot-04-v86.webp',alt:'Resume or manage the Alice in Wonderland audiobook project.',width:1252,height:864},
    {title:'Application Information',desc:'BookStitch application information in the light theme.',src:'assets/screenshot-05-v86.webp',alt:'BookStitch application information in the light theme.',width:1247,height:860},
    {title:'Main Window — Alice in Wonderland',desc:'Alice in Wonderland audiobook with twelve tracks.',src:'assets/screenshot-06-v86.webp',alt:'Alice in Wonderland audiobook with twelve tracks.',width:1251,height:862},
    {title:'Compatibility Metadata — Alice in Wonderland',desc:'Alice in Wonderland compatibility metadata settings.',src:'assets/screenshot-07-v86.webp',alt:'Alice in Wonderland compatibility metadata settings.',width:1250,height:859}
  ];
  let index=0,previousFocus=null,startX=null,flight=null,ghost=null,flightId=0,activeSource=null;
  const title=dialog.querySelector('#gallery-slide-title'),desc=dialog.querySelector('#gallery-slide-desc');
  const count=dialog.querySelector('#gallery-slide-count');
  const screenshot=dialog.querySelector('#gallery-screenshot');
  const dots=[...dialog.querySelectorAll('[data-gallery-index]')];
  function render(){
    const slide=slides[index];
    title.textContent=slide.title;
    desc.textContent=slide.desc;
    count.textContent=`${index+1} / ${slides.length}`;
    screenshot.src=slide.src;
    screenshot.alt=slide.alt;
    screenshot.width=slide.width;
    screenshot.height=slide.height;
    dots.forEach((d,i)=>{
      d.classList.toggle('is-active',i===index);
      if(i===index)d.setAttribute('aria-current','true');
      else d.removeAttribute('aria-current');
    });
  }
  const reducedMotion=window.matchMedia('(prefers-reduced-motion: reduce)');
  function cancelFlight(){
    flightId++;
    if(flight){flight.cancel();flight=null;}
    if(ghost){ghost.remove();ghost=null;}
    if(activeSource)activeSource.style.visibility='';
  }
  async function open(triggerEl,options={}){
    if (!options.fromUrl && location.hash !== '#magic-lantern') history.pushState({bookStitchDetail:true},'','#magic-lantern');
    previousFocus=document.activeElement;
    activeSource=triggerEl.querySelector('.lantern-visual img');
    index=0;render();
    const sourceRect=activeSource.getBoundingClientRect();
    dialog.hidden=false;
    dialog.classList.remove('gallery-landed');
    dialog.setAttribute('aria-hidden','false');
    document.body.classList.add('book-dialog-visible');
    const token=++flightId;
    const landing=dialog.querySelector('.gallery-projector img');
    landing.src=activeSource.currentSrc||activeSource.src;
    if(!options.instant && !reducedMotion.matches && sourceRect.width && sourceRect.height){
      const destination=landing.getBoundingClientRect();
      ghost=document.createElement('img');
      ghost.src=activeSource.currentSrc||activeSource.src;
      ghost.alt='';ghost.setAttribute('aria-hidden','true');
      Object.assign(ghost.style,{
        position:'fixed',left:sourceRect.left+'px',top:sourceRect.top+'px',
        width:sourceRect.width+'px',height:sourceRect.height+'px',
        objectFit:'contain',pointerEvents:'none',zIndex:'1200',transformOrigin:'top left',
        filter:'drop-shadow(0 12px 22px rgba(0,0,0,.48))'
      });
      document.body.appendChild(ghost);
      activeSource.style.visibility='hidden';
      const sx=destination.width/sourceRect.width;
      const sy=destination.height/sourceRect.height;
      flight=ghost.animate([
        {transform:'translate(0px,0px) scale(1,1)',opacity:1},
        {transform:`translate(${destination.left-sourceRect.left}px,${destination.top-sourceRect.top}px) scale(${sx},${sy})`,opacity:1}
      ],{duration:680,easing:'cubic-bezier(.22,.76,.25,1)',fill:'forwards'});
      try{await flight.finished;}catch(_){return;}
      if(token!==flightId)return;
    }
    cancelFlight();
    dialog.classList.add('gallery-landed');
    dialog.querySelector('.gallery-close').focus();
  }
  function close(options={}){if(!options.keepHash && location.hash === '#magic-lantern') history.replaceState(null,'','#magic');cancelFlight();dialog.classList.remove('gallery-landed');dialog.hidden=true;dialog.setAttribute('aria-hidden','true');document.body.classList.remove('book-dialog-visible');previousFocus?.focus();activeSource=null;}
  function move(amount){index=(index+amount+slides.length)%slides.length;render();}
  triggers.forEach(trigger=>trigger.addEventListener('click',()=>open(trigger)));
  function syncGalleryHash(){
    if(location.hash === '#magic-lantern'){
      if(dialog.hidden && triggers[0]) void open(triggers[0],{fromUrl:true,instant:true});
    } else if(!dialog.hidden){close({keepHash:true});}
  }
  window.addEventListener('popstate',syncGalleryHash);
  window.addEventListener('hashchange',syncGalleryHash);
  syncGalleryHash();
  dialog.querySelectorAll('[data-gallery-close]').forEach(el=>el.addEventListener('click',close));
  dialog.querySelector('#gallery-prev').addEventListener('click',()=>move(-1));
  dialog.querySelector('#gallery-next').addEventListener('click',()=>move(1));
  dots.forEach(d=>d.addEventListener('click',()=>{index=Number(d.dataset.galleryIndex);render();}));
  document.addEventListener('keydown',e=>{if(dialog.hidden)return;if(e.key==='Escape'){e.preventDefault();close();}else if(e.key==='ArrowRight'){e.preventDefault();move(1);}else if(e.key==='ArrowLeft'){e.preventDefault();move(-1);}else if(e.key==='Tab'){const els=[...dialog.querySelectorAll('button')];const first=els[0],last=els[els.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}});
  const screen=dialog.querySelector('.gallery-screen-frame');
  screen.addEventListener('touchstart',e=>{startX=e.changedTouches[0].clientX;},{passive:true});
  screen.addEventListener('touchend',e=>{if(startX===null)return;const dx=e.changedTouches[0].clientX-startX;startX=null;if(Math.abs(dx)>45)move(dx<0?1:-1);},{passive:true});
})();



// V68: Hero trust popovers rendered at document-body level to avoid stacking
// contexts from the Hero and section backgrounds; content is compact, no scrollbars.
(() => {
  const ids=['open-source','language','privacy'];
  const pairs=ids.map(id=>({trigger:document.getElementById(id+'-trigger'),panel:document.getElementById(id+'-popover')})).filter(p=>p.trigger&&p.panel);
  pairs.forEach(p=>{document.body.appendChild(p.panel);p.panel.classList.add('trust-floating-v68');});
  const close=(p,focus=false)=>{p.panel.hidden=true;p.trigger.setAttribute('aria-expanded','false');if(focus)p.trigger.focus();};
  const place=p=>{
    const r=p.trigger.getBoundingClientRect(),w=Math.min(415,window.innerWidth-28),gap=9;
    const left=Math.max(14,Math.min(r.left,window.innerWidth-w-14));
    p.panel.style.width=w+'px';
    p.panel.style.left=left+'px';
    p.panel.style.top='0px';
    p.panel.style.visibility='hidden';
    const height=p.panel.getBoundingClientRect().height;
    const below=r.bottom+gap;
    const above=r.top-gap-height;
    const top=(below+height<=window.innerHeight-10||above<10)?below:above;
    p.panel.style.top=Math.max(10,Math.min(top,window.innerHeight-height-10))+'px';
    p.panel.style.visibility='';
  };
  pairs.forEach(p=>p.trigger.addEventListener('click',e=>{
    e.stopPropagation();const open=p.panel.hidden;
    pairs.forEach(close);
    if(open){p.panel.hidden=false;p.trigger.setAttribute('aria-expanded','true');place(p);}
  }));
  document.addEventListener('click',e=>pairs.forEach(p=>{if(!p.trigger.contains(e.target)&&!p.panel.contains(e.target))close(p);}));
  document.addEventListener('keydown',e=>{if(e.key==='Escape')pairs.forEach(p=>{if(!p.panel.hidden)close(p,true);});});
  window.addEventListener('resize',()=>pairs.forEach(p=>{if(!p.panel.hidden)place(p);}));
  window.addEventListener('scroll',()=>pairs.forEach(p=>{if(!p.panel.hidden)close(p);}),{passive:true});
})();
