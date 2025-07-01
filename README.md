# AI Storybook Generator

A service that extracts content from PDFs and EPUBs, stores them in MinIO, and transforms them into interactive storybooks using LLMs.

## ✨ Features

- 📚 **Content Parsing**  
  Render using EPUBJS, PDFJS
  Extracts structured text from PDF and EPUB files using MuPDF.

- ☁️ **Cloud Storage with MinIO**  
  Files are uploaded and served through MinIO with presigned URLs.

- 🤖 **AI Storybook Generation**  
  Generates engaging story formats from extracted text using LLM prompts.

- 🔍 **Metadata & Chaptering**  
  Detects chapter breaks and sections automatically.

## 🛠 Tech Stack

- **.NET** (MVC)  
- **MuPDF** for native file parsing  
- **MinIO** for object storage  
- **OpenAI / LLM integration** *(planned or in progress)*

