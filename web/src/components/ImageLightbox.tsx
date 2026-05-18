export function ImageLightbox({ src, alt, onClose }: { src: string; alt?: string; onClose: () => void }) {
  return (
    <div className="lightbox" onClick={onClose}>
      <button className="lightbox-close" onClick={onClose} aria-label="Close">×</button>
      <img src={src} alt={alt ?? ''} onClick={e => e.stopPropagation()} />
    </div>
  )
}
